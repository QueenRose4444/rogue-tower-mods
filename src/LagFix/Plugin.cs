using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[assembly: AssemblyCompany("RogueTowerLagFix")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyDescription("Late-game CPU, 3x-8x Speed Physics, Infinite-Range AoE/Projectile, UI Canvas, Particle FX, and Audio performance optimizer for Rogue Tower while keeping visual effects enabled.")]
[assembly: AssemblyFileVersion("1.3.0.0")]
[assembly: AssemblyInformationalVersion("1.3.0")]
[assembly: AssemblyProduct("RogueTowerLagFix")]
[assembly: AssemblyTitle("RogueTowerLagFix")]
[assembly: AssemblyVersion("1.3.0.0")]

namespace RogueTowerLagFix;

[BepInPlugin("Rosie.RogueTowerLagFix", "Rogue Tower Late-Game Lag Fix", "1.3.0")]
public class Plugin : BaseUnityPlugin
{
	internal static ManualLogSource Log;

	public static ConfigEntry<bool> OptimizeFastForwardPhysics;
	public static ConfigEntry<bool> OptimizeTargetingAndEnemyLookups;
	public static ConfigEntry<bool> OptimizeEnemyHealthBars;
	public static ConfigEntry<bool> ThrottleDamageNumbers;
	public static ConfigEntry<int> MaxActiveDamageNumbers;
	public static ConfigEntry<float> PerEnemyDamageNumberCooldown;
	public static ConfigEntry<bool> OptimizeProjectileAndExplosionFX;
	public static ConfigEntry<int> MaxActiveProjectileFX;
	public static ConfigEntry<int> MaxActiveExplosions;
	public static ConfigEntry<bool> FixTeslaAndVampireTrailCoroutines;
	public static ConfigEntry<bool> DeduplicateCombatAudio;
	public static ConfigEntry<float> SameSoundCooldownSeconds;
	public static ConfigEntry<int> MaxActiveCombatAudioSources;

	private float _lastTimeScale = -1f;

	private void Awake()
	{
		Log = base.Logger;

		OptimizeFastForwardPhysics = base.Config.Bind(
			"1. CPU & Targeting",
			"OptimizeFastForwardPhysics",
			defaultValue: true,
			"Scales Time.fixedDeltaTime proportionally during 2x/3x speed so Unity runs 50 physics/raycast ticks per real-world second instead of 150 ticks/sec (prevents the 3x speed physics spiral of death).");

		OptimizeTargetingAndEnemyLookups = base.Config.Bind(
			"1. CPU & Targeting",
			"OptimizeTargetingAndEnemyLookups",
			defaultValue: true,
			"Caches Enemy/Pathfinder components per Collider/GameObject, optimizes Tower & Lookout targeting, AoE hit lookups, and enemy walk animations.");

		OptimizeEnemyHealthBars = base.Config.Bind(
			"2. UI & HealthBars",
			"OptimizeEnemyHealthBars",
			defaultValue: true,
			"Throttles World-Space HealthBar and HUD ResourceManager Canvas rebuilds and prevents per-frame string allocations when Bleed/Burn/Poison values have not changed.");

		ThrottleDamageNumbers = base.Config.Bind(
			"3. Damage Numbers",
			"ThrottleDamageNumbers",
			defaultValue: true,
			"Keeps Damage Numbers enabled while coalescing rapid hits on the same enemy into a single updating pop-up and eliminating per-frame UI Canvas mesh rebuilds during spin.");

		MaxActiveDamageNumbers = base.Config.Bind(
			"3. Damage Numbers",
			"MaxActiveDamageNumbers",
			35,
			"Maximum simultaneous floating damage numbers across the screen.");

		PerEnemyDamageNumberCooldown = base.Config.Bind(
			"3. Damage Numbers",
			"PerEnemyDamageNumberCooldown",
			0.30f,
			"Seconds during which multiple hits on the exact same enemy accumulate into its existing floating damage number instead of spawning overlapping Canvases.");

		OptimizeProjectileAndExplosionFX = base.Config.Bind(
			"4. Visual FX",
			"OptimizeProjectileAndExplosionFX",
			defaultValue: true,
			"Skips spawning empty extraProjectileFX when a tower has 0 Bleed/Burn/Poison, caps simultaneous detached particle trails and explosions, and trims post-hit lingering cleanup time so FX stay visible without lag.");

		MaxActiveProjectileFX = base.Config.Bind(
			"4. Visual FX",
			"MaxActiveProjectileFX",
			20,
			"Maximum simultaneous ProjectileFX particle trail objects active at once.");

		MaxActiveExplosions = base.Config.Bind(
			"4. Visual FX",
			"MaxActiveExplosions",
			16,
			"Maximum simultaneous Explosion prefabs active at once.");

		FixTeslaAndVampireTrailCoroutines = base.Config.Bind(
			"4. Visual FX",
			"FixTeslaAndVampireTrailCoroutines",
			defaultValue: true,
			"Prevents Vampire Lair and Tesla Coil from allocating WaitForEndOfFrame GC objects every frame and caps simultaneous lightning/blood trail coroutines at high RPM.");

		DeduplicateCombatAudio = base.Config.Bind(
			"5. Audio",
			"DeduplicateCombatAudio",
			defaultValue: true,
			"Prevents 50+ identical Crit/Hit/Explosion/Coin sounds from spawning simultaneous AudioSources on the exact same millisecond during late-game AoE hits.");

		SameSoundCooldownSeconds = base.Config.Bind(
			"5. Audio",
			"SameSoundCooldownSeconds",
			0.035f,
			"Minimum seconds between two instances of the exact same combat Sound effect.");

		MaxActiveCombatAudioSources = base.Config.Bind(
			"5. Audio",
			"MaxActiveCombatAudioSources",
			32,
			"Maximum simultaneous combat SFX AudioPoolSources playing at once.");

		Time.maximumDeltaTime = 0.1f;
		SceneManager.activeSceneChanged += OnSceneChanged;

		Harmony harmony = new Harmony("Rosie.RogueTowerLagFix");
		harmony.PatchAll();
		Log.LogInfo("RogueTowerLagFix 1.3.0 loaded!");
	}

	private void Update()
	{
		if (OptimizeFastForwardPhysics.Value)
		{
			float ts = Time.timeScale;
			if (ts > 0.01f && Mathf.Abs(ts - _lastTimeScale) > 0.01f)
			{
				_lastTimeScale = ts;
				// Keep FixedUpdate at ~50 ticks per real-world second even at 2x..10x game speed!
				Time.fixedDeltaTime = 0.02f * Mathf.Clamp(ts, 1f, 10f);
				Time.maximumDeltaTime = 0.1f;
			}
		}
		else if (_lastTimeScale != -2f)
		{
			_lastTimeScale = -2f;
			Time.fixedDeltaTime = 0.02f;
			Time.maximumDeltaTime = 0.1f;
		}
		DamageNumberOptimizer.TickActiveNumbers();
		ResourceHUDOptimizer.FlushIfNeeded();
	}

	private void OnSceneChanged(Scene oldScene, Scene newScene)
	{
		_lastTimeScale = -1f;
		EnemyComponentCache.Clear();
		EnemyAndTowerTargetingPatches.ClearDropPathCache();
		HealthBarOptimizer.Clear();
		DamageNumberOptimizer.Clear();
		ParticleAndFXOptimizer.Clear();
		CoroutineLeakPatches.Clear();
		AudioSpamOptimizer.Clear();
		ResourceHUDOptimizer.Clear();
	}
}

internal static class EnemyComponentCache
{
	internal struct CachedEnemy
	{
		public Enemy Enemy;
		public Pathfinder Pathfinder;
	}

	private static readonly Dictionary<int, CachedEnemy> ByColliderId = new Dictionary<int, CachedEnemy>(512);
	private static readonly Dictionary<int, CachedEnemy> ByGameObjectId = new Dictionary<int, CachedEnemy>(512);
	private static readonly Dictionary<int, CachedEnemy> ByTransformId = new Dictionary<int, CachedEnemy>(512);

	internal static void Clear()
	{
		ByColliderId.Clear();
		ByGameObjectId.Clear();
		ByTransformId.Clear();
	}

	internal static void Register(Enemy enemy, Pathfinder pathfinder)
	{
		if (enemy == null)
		{
			return;
		}
		if (pathfinder == null)
		{
			pathfinder = enemy.GetComponent<Pathfinder>();
		}
		CachedEnemy entry = new CachedEnemy
		{
			Enemy = enemy,
			Pathfinder = pathfinder
		};
		GameObject go = enemy.gameObject;
		ByGameObjectId[go.GetInstanceID()] = entry;
		ByTransformId[enemy.transform.GetInstanceID()] = entry;
		Collider col = enemy.GetComponent<Collider>();
		if (col != null)
		{
			ByColliderId[col.GetInstanceID()] = entry;
		}
	}

	internal static void Unregister(Enemy enemy)
	{
		if (enemy == null)
		{
			return;
		}
		ByGameObjectId.Remove(enemy.gameObject.GetInstanceID());
		ByTransformId.Remove(enemy.transform.GetInstanceID());
		Collider col = enemy.GetComponent<Collider>();
		if (col != null)
		{
			ByColliderId.Remove(col.GetInstanceID());
		}
	}

	internal static bool TryGetByCollider(Collider col, out CachedEnemy entry)
	{
		if (col == null)
		{
			entry = default;
			return false;
		}
		int id = col.GetInstanceID();
		if (ByColliderId.TryGetValue(id, out entry))
		{
			if (entry.Enemy != null)
			{
				return true;
			}
			ByColliderId.Remove(id);
		}
		Enemy enemy = col.GetComponent<Enemy>();
		if (enemy != null)
		{
			Pathfinder pf = col.GetComponent<Pathfinder>();
			entry = new CachedEnemy { Enemy = enemy, Pathfinder = pf };
			ByColliderId[id] = entry;
			ByGameObjectId[enemy.gameObject.GetInstanceID()] = entry;
			ByTransformId[enemy.transform.GetInstanceID()] = entry;
			return true;
		}
		entry = default;
		return false;
	}

	internal static Enemy GetByGameObject(GameObject go)
	{
		if (go == null)
		{
			return null;
		}
		int id = go.GetInstanceID();
		if (ByGameObjectId.TryGetValue(id, out var entry))
		{
			if (entry.Enemy != null)
			{
				return entry.Enemy;
			}
			ByGameObjectId.Remove(id);
		}
		Enemy enemy = go.GetComponent<Enemy>();
		if (enemy != null)
		{
			Pathfinder pf = go.GetComponent<Pathfinder>();
			CachedEnemy ce = new CachedEnemy { Enemy = enemy, Pathfinder = pf };
			ByGameObjectId[id] = ce;
			ByTransformId[enemy.transform.GetInstanceID()] = ce;
		}
		return enemy;
	}

	internal static Pathfinder GetPathfinder(Enemy enemy)
	{
		if (enemy == null)
		{
			return null;
		}
		int id = enemy.gameObject.GetInstanceID();
		if (ByGameObjectId.TryGetValue(id, out var entry))
		{
			if (entry.Pathfinder != null)
			{
				return entry.Pathfinder;
			}
		}
		Pathfinder pf = enemy.GetComponent<Pathfinder>();
		if (pf != null)
		{
			CachedEnemy ce = new CachedEnemy { Enemy = enemy, Pathfinder = pf };
			ByGameObjectId[id] = ce;
			ByTransformId[enemy.transform.GetInstanceID()] = ce;
		}
		return pf;
	}
}

[HarmonyPatch]
internal static class EnemyAndTowerTargetingPatches
{
	private static readonly AccessTools.FieldRef<Enemy, Pathfinder> EnemyPathfinderRef =
		AccessTools.FieldRefAccess<Enemy, Pathfinder>("pathfinder");

	private static readonly AccessTools.FieldRef<Tower, GameObject> CurrentTargetRef =
		AccessTools.FieldRefAccess<Tower, GameObject>("currentTarget");
	private static readonly AccessTools.FieldRef<Tower, float> LastTargetUpdateRef =
		AccessTools.FieldRefAccess<Tower, float>("lastTargetUpdate");
	internal static readonly AccessTools.FieldRef<Enemy, bool> EnemyDeadRef =
		AccessTools.FieldRefAccess<Enemy, bool>("dead");

	private static readonly AccessTools.FieldRef<Dropper, float> DropHeightRef =
		AccessTools.FieldRefAccess<Dropper, float>("dropHeight");
	private static readonly AccessTools.FieldRef<Dropper, LayerMask> DropSetMaskRef =
		AccessTools.FieldRefAccess<Dropper, LayerMask>("dropSetMask");
	private static readonly AccessTools.FieldRef<Dropper, Vector2[]> DropPointsRef =
		AccessTools.FieldRefAccess<Dropper, Vector2[]>("dropPoints");
	private static readonly AccessTools.FieldRef<Tower, float> TowerRpsRef =
		AccessTools.FieldRefAccess<Tower, float>("rps");

	private struct PathCell
	{
		public int X;
		public int Z;
	}

	private static readonly List<PathCell> CachedWorldPathCells = new List<PathCell>(512);
	private static int _cachedPathSceneIndex = -1;
	private static int _cachedPathWave = -999;

	internal static void ClearDropPathCache()
	{
		CachedWorldPathCells.Clear();
		_cachedPathSceneIndex = -1;
		_cachedPathWave = -999;
	}

	private static readonly AccessTools.FieldRef<Tower, float> HealthXPRef =
		AccessTools.FieldRefAccess<Tower, float>("<healthXP>k__BackingField");
	private static readonly AccessTools.FieldRef<Tower, float> ArmorXPRef =
		AccessTools.FieldRefAccess<Tower, float>("<armorXP>k__BackingField");
	private static readonly AccessTools.FieldRef<Tower, float> ShieldXPRef =
		AccessTools.FieldRefAccess<Tower, float>("<shieldXP>k__BackingField");

	private static readonly AccessTools.FieldRef<Projectile, float> ProjectileSpeedRef =
		AccessTools.FieldRefAccess<Projectile, float>("speed");
	private static readonly AccessTools.FieldRef<Projectile, LayerMask> ProjectileLayerMaskRef =
		AccessTools.FieldRefAccess<Projectile, LayerMask>("layermask");

	private static readonly AccessTools.FieldRef<WalkAnimator, Pathfinder> WalkPathfinderRef =
		AccessTools.FieldRefAccess<WalkAnimator, Pathfinder>("p");
	private static readonly AccessTools.FieldRef<WalkAnimator, Transform> WalkDirTransformRef =
		AccessTools.FieldRefAccess<WalkAnimator, Transform>("directionalTransform");
	private static readonly AccessTools.FieldRef<WalkAnimator, Transform> WalkArtTransformRef =
		AccessTools.FieldRefAccess<WalkAnimator, Transform>("artTransform");
	private static readonly AccessTools.FieldRef<WalkAnimator, float> WalkStrideLenRef =
		AccessTools.FieldRefAccess<WalkAnimator, float>("strideLength");
	private static readonly AccessTools.FieldRef<WalkAnimator, float> WalkStrideHeightRef =
		AccessTools.FieldRefAccess<WalkAnimator, float>("strideHeight");
	private static readonly AccessTools.FieldRef<WalkAnimator, float> WalkSideAnglesRef =
		AccessTools.FieldRefAccess<WalkAnimator, float>("sideAngles");
	private static readonly AccessTools.FieldRef<WalkAnimator, float> WalkStepRef =
		AccessTools.FieldRefAccess<WalkAnimator, float>("step");
	private static readonly AccessTools.FieldRef<WalkAnimator, Vector3> WalkPrevPosRef =
		AccessTools.FieldRefAccess<WalkAnimator, Vector3>("preiviousPosition");

	private delegate void CheckXPDelegate(Tower instance);
	private static readonly CheckXPDelegate CheckXPAndLevelUpFast =
		(CheckXPDelegate)Delegate.CreateDelegate(
			typeof(CheckXPDelegate),
			typeof(Tower).GetMethod("CheckXPAndLevelUp", BindingFlags.Instance | BindingFlags.NonPublic));

	private delegate void ProjectileOnHitDelegate(Projectile instance, RaycastHit hit);
	private static readonly ProjectileOnHitDelegate ProjectileOnHitFast =
		(ProjectileOnHitDelegate)Delegate.CreateDelegate(
			typeof(ProjectileOnHitDelegate),
			typeof(Projectile).GetMethod("OnHit", BindingFlags.Instance | BindingFlags.NonPublic));

	[HarmonyPatch(typeof(Enemy), "Start")]
	[HarmonyPostfix]
	private static void Enemy_Start_Postfix(Enemy __instance)
	{
		EnemyComponentCache.Register(__instance, EnemyPathfinderRef(__instance));
	}

	[HarmonyPatch(typeof(Enemy), "Die")]
	[HarmonyPostfix]
	private static void Enemy_Die_Postfix(Enemy __instance)
	{
		EnemyComponentCache.Unregister(__instance);
		DamageNumberOptimizer.OnEnemyRemoved(__instance);
		HealthBarOptimizer.OnEnemyRemoved(__instance);
	}

	[HarmonyPatch(typeof(Enemy), "AtEnd")]
	[HarmonyPostfix]
	private static void Enemy_AtEnd_Postfix(Enemy __instance)
	{
		EnemyComponentCache.Unregister(__instance);
		DamageNumberOptimizer.OnEnemyRemoved(__instance);
		HealthBarOptimizer.OnEnemyRemoved(__instance);
	}

	// Ensure SnowFlake never tunnels when Time.fixedDeltaTime is scaled at 2x/3x speed
	[HarmonyPatch(typeof(SnowFlake), "CheckForHits")]
	[HarmonyPrefix]
	private static bool SnowFlake_CheckForHits_Prefix(SnowFlake __instance)
	{
		if (!Plugin.OptimizeFastForwardPhysics.Value)
		{
			return true;
		}
		float spd = ProjectileSpeedRef(__instance);
		float castDist = Mathf.Max(0.25f, spd * Time.fixedDeltaTime + 0.15f);
		if (Physics.SphereCast(__instance.transform.position, 0.125f, Vector3.down, out var hitInfo, castDist, ProjectileLayerMaskRef(__instance), QueryTriggerInteraction.Collide))
		{
			ProjectileOnHitFast(__instance, hitInfo);
		}
		return false;
	}

	// Optimize WalkAnimator in late game / 2x-3x speed by skipping redundant every-tick LookRotation and stepping at half rate when >30 enemies are active
	[HarmonyPatch(typeof(WalkAnimator), "FixedUpdate")]
	[HarmonyPrefix]
	private static bool WalkAnimator_FixedUpdate_Prefix(WalkAnimator __instance)
	{
		if (!Plugin.OptimizeTargetingAndEnemyLookups.Value)
		{
			return true;
		}
		int enemyCount = (SpawnManager.instance != null) ? SpawnManager.instance.currentEnemies.Count : 0;
		bool halfRate = enemyCount > 30 || Time.timeScale > 1.5f;
		if (halfRate && ((Time.frameCount + __instance.GetInstanceID()) & 1) != 0)
		{
			return false;
		}

		Transform tr = __instance.transform;
		Vector3 pos = tr.position;
		ref Vector3 prevPos = ref WalkPrevPosRef(__instance);
		Vector3 dir = pos - prevPos;
		prevPos = pos;

		if (dir.sqrMagnitude > 0.0001f)
		{
			Transform dirTr = WalkDirTransformRef(__instance);
			Vector3 fwd = dirTr.forward;
			// Only call Quaternion.LookRotation when the enemy actually turns at a corner
			if (Vector3.Dot(fwd, dir.normalized) < 0.995f)
			{
				dirTr.rotation = Quaternion.LookRotation(dir, Vector3.up);
			}
		}

		Pathfinder pf = WalkPathfinderRef(__instance);
		if (pf != null)
		{
			float dt = halfRate ? (Time.fixedDeltaTime * 2f) : Time.fixedDeltaTime;
			ref float step = ref WalkStepRef(__instance);
			step = (step + pf.speed * dt / WalkStrideLenRef(__instance)) % 2f;
			float rad = step * (float)Math.PI;
			Transform art = WalkArtTransformRef(__instance);
			art.localPosition = new Vector3(0f, WalkStrideHeightRef(__instance) * Mathf.Abs(Mathf.Cos(rad)), 0f);
			art.localEulerAngles = new Vector3(WalkSideAnglesRef(__instance) * Mathf.Sin(rad), 0f, 0f);
		}
		return false;
	}

	private static float FastIntPow(float val, int exp)
	{
		return exp switch
		{
			1 => val,
			2 => val * val,
			3 => val * val * val,
			_ => Mathf.Pow(val, exp),
		};
	}

	// Bypass Physics.OverlapSphere when InfiniteRange/large-range or moderate enemy counts make direct iteration of SpawnManager.currentEnemies O(N_living) with 0 PhysX & GC cost
	[HarmonyPatch(typeof(Tower), "DetectEnemies")]
	[HarmonyPrefix]
	private static bool Tower_DetectEnemies_Prefix(Tower __instance)
	{
		if (!Plugin.OptimizeTargetingAndEnemyLookups.Value ||
			SpawnManager.instance == null ||
			SpawnManager.instance.currentEnemies == null)
		{
			return true;
		}

		HashSet<Enemy> enemies = SpawnManager.instance.currentEnemies;
		int count = enemies.Count;
		if (count == 0)
		{
			CurrentTargetRef(__instance) = null;
			LastTargetUpdateRef(__instance) = UnityEngine.Random.Range(0.25f, 1f);
			return false;
		}

		if (__instance.range < 35f && count > 64)
		{
			return true;
		}

		Vector3 towerPos = __instance.transform.position;
		float maxDist = __instance.range + 0.5f;
		float maxDistSq = maxDist * maxDist;
		Lookout selfLookout = __instance as Lookout;

		GameObject bestTarget = null;
		float bestScore = -1f;

		if (selfLookout != null)
		{
			bool pProgress = false, pNearDeath = false, pMostHealth = false, pMostArmor = false, pMostShield = false;
			bool pLeastHealth = false, pLeastArmor = false, pLeastShield = false, pFastest = false, pSlowest = false;

			Tower.Priority[] priorities = selfLookout.priorities;
			for (int p = 0; p < priorities.Length; p++)
			{
				switch (priorities[p])
				{
				case Tower.Priority.Progress: pProgress = true; break;
				case Tower.Priority.NearDeath: pNearDeath = true; break;
				case Tower.Priority.MostHealth: pMostHealth = true; break;
				case Tower.Priority.MostArmor: pMostArmor = true; break;
				case Tower.Priority.MostShield: pMostShield = true; break;
				case Tower.Priority.LeastHealth: pLeastHealth = true; break;
				case Tower.Priority.LeastArmor: pLeastArmor = true; break;
				case Tower.Priority.LeastShield: pLeastShield = true; break;
				case Tower.Priority.Fastest: pFastest = true; break;
				case Tower.Priority.Slowest: pSlowest = true; break;
				}
			}

			bool needPf = pProgress || pFastest || pSlowest;
			foreach (Enemy enemy in enemies)
			{
				if (enemy == null || EnemyDeadRef(enemy) || (enemy.mark != null && enemy.mark != selfLookout))
				{
					continue;
				}
				Vector3 delta = enemy.transform.position - towerPos;
				if (delta.sqrMagnitude > maxDistSq)
				{
					continue;
				}

				Pathfinder pf = needPf ? EnemyComponentCache.GetPathfinder(enemy) : null;
				float score = 1f;
				if (pProgress && pf != null) score /= Mathf.Max(0.001f, pf.distanceFromEnd);
				if (pNearDeath) score /= Mathf.Max(0.001f, enemy.CurrentHealth());
				if (pMostHealth) score *= Mathf.Max(1, enemy.health);
				if (pMostArmor) score *= Mathf.Max(1, enemy.armor);
				if (pMostShield) score *= Mathf.Max(1, enemy.shield);
				if (pLeastHealth) score /= Mathf.Max(1, enemy.health);
				if (pLeastArmor) score /= Mathf.Max(1, enemy.armor);
				if (pLeastShield) score /= Mathf.Max(1, enemy.shield);
				if (pFastest && pf != null) score *= Mathf.Max(0.001f, pf.speed);
				if (pSlowest && pf != null) score /= Mathf.Max(0.001f, pf.speed);

				if (score > bestScore)
				{
					bestTarget = enemy.gameObject;
					bestScore = score;
				}
			}
		}
		else
		{
			int pProgress = 0, pNearDeath = 0, pMostHealth = 0, pMostArmor = 0, pMostShield = 0;
			int pLeastHealth = 0, pLeastArmor = 0, pLeastShield = 0, pFastest = 0, pSlowest = 0, pMarked = 0;

			Tower.Priority[] priorities = __instance.priorities;
			for (int p = priorities.Length - 1; p >= 0; p--)
			{
				int scale = Mathf.Clamp(3 - p, 1, 3);
				switch (priorities[p])
				{
				case Tower.Priority.Progress: pProgress = scale; break;
				case Tower.Priority.NearDeath: pNearDeath = scale; break;
				case Tower.Priority.MostHealth: pMostHealth = scale; break;
				case Tower.Priority.MostArmor: pMostArmor = scale; break;
				case Tower.Priority.MostShield: pMostShield = scale; break;
				case Tower.Priority.LeastHealth: pLeastHealth = scale; break;
				case Tower.Priority.LeastArmor: pLeastArmor = scale; break;
				case Tower.Priority.LeastShield: pLeastShield = scale; break;
				case Tower.Priority.Fastest: pFastest = scale; break;
				case Tower.Priority.Slowest: pSlowest = scale; break;
				case Tower.Priority.Marked: pMarked = scale; break;
				}
			}

			bool needPf = pProgress > 0 || pFastest > 0 || pSlowest > 0;
			foreach (Enemy enemy in enemies)
			{
				if (enemy == null || EnemyDeadRef(enemy))
				{
					continue;
				}
				Vector3 delta = enemy.transform.position - towerPos;
				if (delta.sqrMagnitude > maxDistSq)
				{
					continue;
				}

				Pathfinder pf = needPf ? EnemyComponentCache.GetPathfinder(enemy) : null;
				float score = 1f;

				if (pProgress > 0 && pf != null)
				{
					score /= FastIntPow(Mathf.Max(0.001f, pf.distanceFromEnd), pProgress);
				}
				if (pNearDeath > 0)
				{
					score /= FastIntPow(Mathf.Max(0.001f, enemy.CurrentHealth()), pNearDeath);
				}
				if (pMostHealth > 0)
				{
					score *= FastIntPow((float)Mathf.Max(1, enemy.health) * 0.001f, pMostHealth);
				}
				if (pMostArmor > 0)
				{
					score *= FastIntPow((float)Mathf.Max(1, enemy.armor) * 0.001f, pMostArmor);
				}
				if (pMostShield > 0)
				{
					score *= FastIntPow((float)Mathf.Max(1, enemy.shield) * 0.001f, pMostShield);
				}
				if (pLeastHealth > 0)
				{
					score /= FastIntPow(Mathf.Max(1, enemy.health), pLeastHealth);
				}
				if (pLeastArmor > 0)
				{
					score /= FastIntPow(Mathf.Max(1, enemy.armor), pLeastArmor);
				}
				if (pLeastShield > 0)
				{
					score /= FastIntPow(Mathf.Max(1, enemy.shield), pLeastShield);
				}
				if (pFastest > 0 && pf != null)
				{
					score *= FastIntPow(Mathf.Max(0.001f, pf.speed), pFastest);
				}
				if (pSlowest > 0 && pf != null)
				{
					score /= FastIntPow(Mathf.Max(0.001f, pf.speed), pSlowest);
				}
				if (pMarked > 0)
				{
					float f11 = 1f;
					Lookout mark = enemy.mark;
					if (mark != null)
					{
						f11 = (float)(mark.damage * (mark.healthDamage + mark.armorDamage + mark.shieldDamage)) * (1f + mark.critChance);
					}
					score *= FastIntPow(f11, pMarked);
				}

				if (score > bestScore)
				{
					bestTarget = enemy.gameObject;
					bestScore = score;
				}
			}
		}

		CurrentTargetRef(__instance) = bestTarget;
		LastTargetUpdateRef(__instance) = UnityEngine.Random.Range(0.25f, 1f);
		return false;
	}

	// Replace (2*range+1)^2 = 251,001 raycasts per Dropper on InfiniteRange wave changes with one cached world path-cell table per wave
	[HarmonyPatch(typeof(Dropper), "SetDropPoints")]
	[HarmonyPrefix]
	private static bool Dropper_SetDropPoints_Prefix(Dropper __instance)
	{
		if (!Plugin.OptimizeTargetingAndEnemyLookups.Value || __instance.range <= 25f)
		{
			return true;
		}

		int sceneIdx = SceneManager.GetActiveScene().buildIndex;
		int wave = (SpawnManager.instance != null) ? SpawnManager.instance.level : -1;
		float dropHeight = DropHeightRef(__instance);
		LayerMask dropSetMask = DropSetMaskRef(__instance);

		if (sceneIdx != _cachedPathSceneIndex || wave != _cachedPathWave)
		{
			_cachedPathSceneIndex = sceneIdx;
			_cachedPathWave = wave;
			CachedWorldPathCells.Clear();

			int pathLayer = LayerMask.NameToLayer("Path");
			Collider[] allCols = UnityEngine.Object.FindObjectsOfType<Collider>();
			HashSet<long> candidateKeys = new HashSet<long>();
			for (int i = 0; i < allCols.Length; i++)
			{
				Collider c = allCols[i];
				if (c == null || c.gameObject.layer != pathLayer)
				{
					continue;
				}
				Bounds b = c.bounds;
				int minX = Mathf.FloorToInt(b.min.x);
				int maxX = Mathf.CeilToInt(b.max.x);
				int minZ = Mathf.FloorToInt(b.min.z);
				int maxZ = Mathf.CeilToInt(b.max.z);
				for (int wx = minX; wx <= maxX; wx++)
				{
					for (int wz = minZ; wz <= maxZ; wz++)
					{
						long key = (((long)wx + 10000L) << 20) | ((long)wz + 10000L);
						if (!candidateKeys.Add(key))
						{
							continue;
						}
						if (Physics.Raycast(new Vector3(wx, dropHeight, wz), Vector3.down, out var hitInfo, 1.5f * dropHeight, dropSetMask, QueryTriggerInteraction.Ignore) &&
							hitInfo.transform.gameObject.layer == pathLayer)
						{
							CachedWorldPathCells.Add(new PathCell { X = wx, Z = wz });
						}
					}
				}
			}
		}

		int tx = Mathf.RoundToInt(__instance.transform.position.x);
		int tz = Mathf.RoundToInt(__instance.transform.position.z);
		int rInt = (int)__instance.range;
		List<Vector2> localPts = new List<Vector2>(CachedWorldPathCells.Count);
		for (int i = 0; i < CachedWorldPathCells.Count; i++)
		{
			PathCell cell = CachedWorldPathCells[i];
			int dx = cell.X - tx;
			int dz = cell.Z - tz;
			if (Mathf.Abs(dx) <= rInt && Mathf.Abs(dz) <= rInt)
			{
				localPts.Add(new Vector2(dx, dz));
			}
		}

		Vector2[] arr = localPts.ToArray();
		DropPointsRef(__instance) = arr;
		TowerRpsRef(__instance) = 60f / __instance.rpm * (10f / (10f + (float)arr.Length));
		__instance.dropperRPMdisplay = __instance.rpm * (10f + (float)arr.Length) / 10f;
		return false;
	}

	[HarmonyPatch(typeof(Tower), "SelectEnemy")]
	[HarmonyPrefix]
	private static bool Tower_SelectEnemy_Prefix(Tower __instance, Collider[] possibleTargets, ref GameObject __result)
	{
		if (!Plugin.OptimizeTargetingAndEnemyLookups.Value)
		{
			return true;
		}
		if (possibleTargets == null || possibleTargets.Length == 0)
		{
			__result = null;
			return false;
		}

		int pProgress = 0, pNearDeath = 0, pMostHealth = 0, pMostArmor = 0, pMostShield = 0;
		int pLeastHealth = 0, pLeastArmor = 0, pLeastShield = 0, pFastest = 0, pSlowest = 0, pMarked = 0;

		Tower.Priority[] priorities = __instance.priorities;
		for (int p = priorities.Length - 1; p >= 0; p--)
		{
			int scale = Mathf.Clamp(3 - p, 1, 3);
			switch (priorities[p])
			{
			case Tower.Priority.Progress: pProgress = scale; break;
			case Tower.Priority.NearDeath: pNearDeath = scale; break;
			case Tower.Priority.MostHealth: pMostHealth = scale; break;
			case Tower.Priority.MostArmor: pMostArmor = scale; break;
			case Tower.Priority.MostShield: pMostShield = scale; break;
			case Tower.Priority.LeastHealth: pLeastHealth = scale; break;
			case Tower.Priority.LeastArmor: pLeastArmor = scale; break;
			case Tower.Priority.LeastShield: pLeastShield = scale; break;
			case Tower.Priority.Fastest: pFastest = scale; break;
			case Tower.Priority.Slowest: pSlowest = scale; break;
			case Tower.Priority.Marked: pMarked = scale; break;
			}
		}

		GameObject bestTarget = null;
		float bestScore = -1f;

		for (int i = 0; i < possibleTargets.Length; i++)
		{
			Collider col = possibleTargets[i];
			if (!EnemyComponentCache.TryGetByCollider(col, out var cached))
			{
				continue;
			}
			Enemy enemy = cached.Enemy;
			Pathfinder pf = cached.Pathfinder;
			float score = 1f;

			if (pProgress > 0 && pf != null)
			{
				float f = Mathf.Max(0.001f, pf.distanceFromEnd);
				score /= FastIntPow(f, pProgress);
			}
			if (pNearDeath > 0)
			{
				float f2 = Mathf.Max(0.001f, enemy.CurrentHealth());
				score /= FastIntPow(f2, pNearDeath);
			}
			if (pMostHealth > 0)
			{
				float f3 = (float)Mathf.Max(1, enemy.health) * 0.001f;
				score *= FastIntPow(f3, pMostHealth);
			}
			if (pMostArmor > 0)
			{
				float f4 = (float)Mathf.Max(1, enemy.armor) * 0.001f;
				score *= FastIntPow(f4, pMostArmor);
			}
			if (pMostShield > 0)
			{
				float f5 = (float)Mathf.Max(1, enemy.shield) * 0.001f;
				score *= FastIntPow(f5, pMostShield);
			}
			if (pLeastHealth > 0)
			{
				float f6 = Mathf.Max(1, enemy.health);
				score /= FastIntPow(f6, pLeastHealth);
			}
			if (pLeastArmor > 0)
			{
				float f7 = Mathf.Max(1, enemy.armor);
				score /= FastIntPow(f7, pLeastArmor);
			}
			if (pLeastShield > 0)
			{
				float f8 = Mathf.Max(1, enemy.shield);
				score /= FastIntPow(f8, pLeastShield);
			}
			if (pFastest > 0 && pf != null)
			{
				float f9 = Mathf.Max(0.001f, pf.speed);
				score *= FastIntPow(f9, pFastest);
			}
			if (pSlowest > 0 && pf != null)
			{
				float f10 = Mathf.Max(0.001f, pf.speed);
				score /= FastIntPow(f10, pSlowest);
			}
			if (pMarked > 0)
			{
				float f11 = 1f;
				Lookout mark = enemy.mark;
				if (mark != null)
				{
					f11 = (float)(mark.damage * (mark.healthDamage + mark.armorDamage + mark.shieldDamage)) * (1f + mark.critChance);
				}
				score *= FastIntPow(f11, pMarked);
			}

			if (score > bestScore)
			{
				bestTarget = enemy.gameObject;
				bestScore = score;
			}
		}

		__result = bestTarget;
		return false;
	}

	[HarmonyPatch(typeof(Lookout), "SelectEnemy")]
	[HarmonyPrefix]
	private static bool Lookout_SelectEnemy_Prefix(Lookout __instance, Collider[] possibleTargets, ref GameObject __result)
	{
		if (!Plugin.OptimizeTargetingAndEnemyLookups.Value)
		{
			return true;
		}
		if (possibleTargets == null || possibleTargets.Length == 0)
		{
			__result = null;
			return false;
		}

		bool pProgress = false, pNearDeath = false, pMostHealth = false, pMostArmor = false, pMostShield = false;
		bool pLeastHealth = false, pLeastArmor = false, pLeastShield = false, pFastest = false, pSlowest = false;

		Tower.Priority[] priorities = __instance.priorities;
		for (int p = 0; p < priorities.Length; p++)
		{
			switch (priorities[p])
			{
			case Tower.Priority.Progress: pProgress = true; break;
			case Tower.Priority.NearDeath: pNearDeath = true; break;
			case Tower.Priority.MostHealth: pMostHealth = true; break;
			case Tower.Priority.MostArmor: pMostArmor = true; break;
			case Tower.Priority.MostShield: pMostShield = true; break;
			case Tower.Priority.LeastHealth: pLeastHealth = true; break;
			case Tower.Priority.LeastArmor: pLeastArmor = true; break;
			case Tower.Priority.LeastShield: pLeastShield = true; break;
			case Tower.Priority.Fastest: pFastest = true; break;
			case Tower.Priority.Slowest: pSlowest = true; break;
			}
		}

		GameObject bestTarget = null;
		float bestScore = -1f;

		for (int i = 0; i < possibleTargets.Length; i++)
		{
			if (!EnemyComponentCache.TryGetByCollider(possibleTargets[i], out var cached))
			{
				continue;
			}
			Enemy enemy = cached.Enemy;
			if (enemy.mark != null && enemy.mark != __instance)
			{
				continue;
			}
			Pathfinder pf = cached.Pathfinder;
			float score = 1f;
			if (pProgress && pf != null) score /= Mathf.Max(0.001f, pf.distanceFromEnd);
			if (pNearDeath) score /= Mathf.Max(0.001f, enemy.CurrentHealth());
			if (pMostHealth) score *= Mathf.Max(1, enemy.health);
			if (pMostArmor) score *= Mathf.Max(1, enemy.armor);
			if (pMostShield) score *= Mathf.Max(1, enemy.shield);
			if (pLeastHealth) score /= Mathf.Max(1, enemy.health);
			if (pLeastArmor) score /= Mathf.Max(1, enemy.armor);
			if (pLeastShield) score /= Mathf.Max(1, enemy.shield);
			if (pFastest && pf != null) score *= Mathf.Max(0.001f, pf.speed);
			if (pSlowest && pf != null) score /= Mathf.Max(0.001f, pf.speed);

			if (score > bestScore)
			{
				bestTarget = enemy.gameObject;
				bestScore = score;
			}
		}

		__result = bestTarget;
		return false;
	}

	private static readonly AccessTools.FieldRef<FlameThrower, bool> FlameHasManaRef =
		AccessTools.FieldRefAccess<FlameThrower, bool>("hasMana");
	private static readonly AccessTools.FieldRef<Tower, Transform> TowerMuzzleRef =
		AccessTools.FieldRefAccess<Tower, Transform>("muzzle");
	private static readonly AccessTools.FieldRef<Tower, LayerMask> TowerEnemyMaskTopRef =
		AccessTools.FieldRefAccess<Tower, LayerMask>("enemyLayerMask");

	private static readonly AccessTools.FieldRef<Obelisk, IDamageable> ObeliskLastHitRef =
		AccessTools.FieldRefAccess<Obelisk, IDamageable>("lastThingIHit");
	private static readonly AccessTools.FieldRef<Obelisk, float> ObeliskTimeOnTargetRef =
		AccessTools.FieldRefAccess<Obelisk, float>("timeOnTarget");
	private static readonly AccessTools.FieldRef<Obelisk, float> ObeliskRpsRef =
		AccessTools.FieldRefAccess<Obelisk, float>("rps");
	private static readonly AccessTools.FieldRef<Obelisk, GameObject> ObeliskBeamRef =
		AccessTools.FieldRefAccess<Obelisk, GameObject>("beam");
	private delegate void ObeliskPlaySoundDelegate(Obelisk instance, bool onOff);
	private static readonly ObeliskPlaySoundDelegate ObeliskPlaySoundFast =
		(ObeliskPlaySoundDelegate)Delegate.CreateDelegate(
			typeof(ObeliskPlaySoundDelegate),
			typeof(Obelisk).GetMethod("PlaySound", BindingFlags.Instance | BindingFlags.NonPublic));

	private static readonly AccessTools.FieldRef<Projectile, float> ProjectileMaxLifeRef =
		AccessTools.FieldRefAccess<Projectile, float>("maximumLifeTime");

	[HarmonyPatch(typeof(Tower), "GainXP")]
	[HarmonyPrefix]
	private static bool Tower_GainXP_Prefix(Tower __instance)
	{
		if (!Plugin.OptimizeTargetingAndEnemyLookups.Value)
		{
			return true;
		}
		GameObject target = CurrentTargetRef(__instance);
		Enemy enemy = EnemyComponentCache.GetByGameObject(target);
		if (enemy != null)
		{
			float dt = Time.deltaTime;
			float xpGain = dt / (2f * __instance.range) + dt * 0.5f;
			if (enemy.shield > 0)
			{
				ShieldXPRef(__instance) += xpGain;
			}
			else if (enemy.armor > 0)
			{
				ArmorXPRef(__instance) += xpGain;
			}
			else
			{
				HealthXPRef(__instance) += xpGain;
			}
			CheckXPAndLevelUpFast(__instance);
		}
		return false;
	}

	// Cap InfiniteRange FlameThrower SphereCastAll bounds (avoids sweeping a 41m-radius x 375m tube across empty space) and use EnemyComponentCache
	[HarmonyPatch(typeof(FlameThrower), "Fire")]
	[HarmonyPrefix]
	private static bool FlameThrower_Fire_Prefix(FlameThrower __instance)
	{
		if (!Plugin.OptimizeTargetingAndEnemyLookups.Value)
		{
			return true;
		}
		if (__instance.consumesMana)
		{
			int manaCost = (int)((float)__instance.damage * __instance.manaConsumptionRate);
			if (!ResourceManager.instance.CheckMana(manaCost))
			{
				FlameHasManaRef(__instance) = false;
				return false;
			}
			ResourceManager.instance.SpendMana(manaCost);
			FlameHasManaRef(__instance) = true;
		}
		Transform muzzle = TowerMuzzleRef(__instance);
		float radius = Mathf.Min(__instance.range / 6f, 8f);
		float dist = Mathf.Min(__instance.range * 1.5f, 95f);
		RaycastHit[] hits = Physics.SphereCastAll(muzzle.position, radius, muzzle.forward, dist, TowerEnemyMaskTopRef(__instance), QueryTriggerInteraction.Collide);
		for (int i = 0; i < hits.Length; i++)
		{
			Collider col = hits[i].collider;
			if (EnemyComponentCache.TryGetByCollider(col, out var cached))
			{
				cached.Enemy.TakeDamage(__instance.towerType, __instance.damage, __instance.healthDamage, __instance.armorDamage, __instance.shieldDamage, __instance.slowPercent, __instance.bleedPercent, __instance.burnPercent, __instance.poisonPercent, __instance.critChance, __instance.stunChance);
			}
			else
			{
				col?.GetComponent<IDamageable>()?.TakeDamage(__instance.towerType, __instance.damage, __instance.healthDamage, __instance.armorDamage, __instance.shieldDamage, __instance.slowPercent, __instance.bleedPercent, __instance.burnPercent, __instance.poisonPercent, __instance.critChance, __instance.stunChance);
			}
		}
		return false;
	}

	// Cache Enemy lookup inside Obelisk.DealDamage (every beam tick)
	[HarmonyPatch(typeof(Obelisk), "DealDamage")]
	[HarmonyPrefix]
	private static bool Obelisk_DealDamage_Prefix(Obelisk __instance)
	{
		if (!Plugin.OptimizeTargetingAndEnemyLookups.Value)
		{
			return true;
		}
		GameObject targetGo = CurrentTargetRef(__instance);
		IDamageable dmgTarget = EnemyComponentCache.GetByGameObject(targetGo);
		if (dmgTarget == null && targetGo != null)
		{
			dmgTarget = targetGo.GetComponent<IDamageable>();
		}
		if (dmgTarget != null)
		{
			ref IDamageable last = ref ObeliskLastHitRef(__instance);
			ref float timeOn = ref ObeliskTimeOnTargetRef(__instance);
			if (dmgTarget == last)
			{
				timeOn += ObeliskRpsRef(__instance);
			}
			else
			{
				timeOn = 0f;
				last = dmgTarget;
			}
			int bonus = Mathf.Clamp(Mathf.FloorToInt(timeOn * TowerManager.instance.obeliskTimeOnTargetMultiplier), 0, __instance.damage);
			dmgTarget.TakeDamage(__instance.towerType, __instance.damage + bonus, __instance.healthDamage, __instance.armorDamage, __instance.shieldDamage, __instance.slowPercent, __instance.bleedPercent, __instance.burnPercent, __instance.poisonPercent, __instance.critChance, __instance.stunChance);
			ObeliskBeamRef(__instance).SetActive(value: true);
			ObeliskPlaySoundFast(__instance, true);
		}
		return false;
	}

	// Prevent InfiniteRange Cannonballs from flying for 30+ seconds into the void executing SphereCastAll every physics tick
	[HarmonyPatch(typeof(Cannonball), nameof(Cannonball.SetRange))]
	[HarmonyPostfix]
	private static void Cannonball_SetRange_Postfix(Cannonball __instance, float range)
	{
		if (Plugin.OptimizeProjectileAndExplosionFX.Value && range > 40f && __instance != null)
		{
			ref float life = ref ProjectileMaxLifeRef(__instance);
			if (life > 4.5f)
			{
				life = 4.5f;
			}
		}
	}
}

[HarmonyPatch(typeof(ResourceManager))]
internal static class ResourceHUDOptimizer
{
	private static readonly AccessTools.FieldRef<ResourceManager, int> GoldRef = AccessTools.FieldRefAccess<ResourceManager, int>("gold");
	private static readonly AccessTools.FieldRef<ResourceManager, int> ManaRef = AccessTools.FieldRefAccess<ResourceManager, int>("mana");
	private static readonly AccessTools.FieldRef<ResourceManager, int> ManaGatherRef = AccessTools.FieldRefAccess<ResourceManager, int>("manaGatherRate");
	private static readonly AccessTools.FieldRef<ResourceManager, Text> GoldTextRef = AccessTools.FieldRefAccess<ResourceManager, Text>("goldText");
	private static readonly AccessTools.FieldRef<ResourceManager, Text> ManaTextRef = AccessTools.FieldRefAccess<ResourceManager, Text>("manaText");
	private static readonly AccessTools.FieldRef<ResourceManager, Image> ManaBarRef = AccessTools.FieldRefAccess<ResourceManager, Image>("manaBar");

	private static int _lastGold = int.MinValue;
	private static int _lastMana = int.MinValue;
	private static int _lastMaxMana = int.MinValue;
	private static int _lastTotalRegen = int.MinValue;
	private static float _lastUpdateUnscaledTime;
	private static bool _dirty;

	internal static void Clear()
	{
		_lastGold = int.MinValue;
		_lastMana = int.MinValue;
		_lastMaxMana = int.MinValue;
		_lastTotalRegen = int.MinValue;
		_lastUpdateUnscaledTime = 0f;
		_dirty = false;
	}

	internal static void FlushIfNeeded()
	{
		if (!_dirty || ResourceManager.instance == null)
		{
			return;
		}
		if (Time.unscaledTime - _lastUpdateUnscaledTime >= 0.05f)
		{
			ApplyNow(ResourceManager.instance);
		}
	}

	private static void ApplyNow(ResourceManager rm)
	{
		_dirty = false;
		_lastUpdateUnscaledTime = Time.unscaledTime;

		int gold = GoldRef(rm);
		int mana = ManaRef(rm);
		int maxMana = rm.maxMana;
		int totalRegen = ManaGatherRef(rm) + Mathf.FloorToInt(rm.manaMaxRegenPercent * (float)maxMana);

		if (gold != _lastGold)
		{
			_lastGold = gold;
			GoldTextRef(rm).text = "Gold: " + gold;
		}
		if (mana != _lastMana || maxMana != _lastMaxMana || totalRegen != _lastTotalRegen)
		{
			_lastMana = mana;
			_lastMaxMana = maxMana;
			_lastTotalRegen = totalRegen;
			ManaTextRef(rm).text = "Mana: " + mana + "/" + maxMana + " (+" + totalRegen + "/s)";
			float div = (maxMana >= 500) ? 100f : 20f;
			ManaBarRef(rm).rectTransform.sizeDelta = new Vector2((float)mana / div, 0.25f);
		}
	}

	[HarmonyPatch("UpdateResourceText")]
	[HarmonyPrefix]
	private static bool UpdateResourceText_Prefix(ResourceManager __instance)
	{
		if (!Plugin.OptimizeEnemyHealthBars.Value)
		{
			return true;
		}
		if (Time.unscaledTime - _lastUpdateUnscaledTime >= 0.05f)
		{
			ApplyNow(__instance);
		}
		else
		{
			_dirty = true;
		}
		return false;
	}
}

[HarmonyPatch(typeof(HealthBar))]
internal static class HealthBarOptimizer
{
	private sealed class BarState
	{
		public int LastHeal = -1;
		public int LastArmor = -1;
		public int LastShield = -1;
		public int LastBleeding = -1;
		public int LastBleed = -1;
		public int LastBurning = -1;
		public int LastBurn = -1;
		public int LastPoisoned = -1;
		public int LastPoison = -1;
		public float LastSlow = -1f;
		public float LastFort = -1f;
		public float LastHaste = -1f;
		public float LastBarUpdateTime;
	}

	private static readonly Dictionary<int, BarState> States = new Dictionary<int, BarState>(512);

	private static readonly AccessTools.FieldRef<Enemy, HealthBar> EnemyHealthBarRef = AccessTools.FieldRefAccess<Enemy, HealthBar>("healthBar");
	private static readonly AccessTools.FieldRef<HealthBar, float> BarScalerRef = AccessTools.FieldRefAccess<HealthBar, float>("barScaler");
	private static readonly AccessTools.FieldRef<HealthBar, float> HealthRef = AccessTools.FieldRefAccess<HealthBar, float>("health");
	private static readonly AccessTools.FieldRef<HealthBar, float> ArmorRef = AccessTools.FieldRefAccess<HealthBar, float>("armor");
	private static readonly AccessTools.FieldRef<HealthBar, float> ShieldRef = AccessTools.FieldRefAccess<HealthBar, float>("shield");
	private static readonly AccessTools.FieldRef<HealthBar, float> DmgRef = AccessTools.FieldRefAccess<HealthBar, float>("dmg");
	private static readonly AccessTools.FieldRef<HealthBar, Image> MaskBarRef = AccessTools.FieldRefAccess<HealthBar, Image>("maskBar");
	private static readonly AccessTools.FieldRef<HealthBar, Image> HealthBarImgRef = AccessTools.FieldRefAccess<HealthBar, Image>("healthBar");
	private static readonly AccessTools.FieldRef<HealthBar, Image> ArmorBarImgRef = AccessTools.FieldRefAccess<HealthBar, Image>("armorBar");
	private static readonly AccessTools.FieldRef<HealthBar, Image> ShieldBarImgRef = AccessTools.FieldRefAccess<HealthBar, Image>("shieldBar");
	private static readonly AccessTools.FieldRef<HealthBar, Image> DmgBarImgRef = AccessTools.FieldRefAccess<HealthBar, Image>("dmgBar");
	private static readonly AccessTools.FieldRef<HealthBar, Image> SlowLeftRef = AccessTools.FieldRefAccess<HealthBar, Image>("slowImageLeft");
	private static readonly AccessTools.FieldRef<HealthBar, Image> SlowRightRef = AccessTools.FieldRefAccess<HealthBar, Image>("slowImageRight");
	private static readonly AccessTools.FieldRef<HealthBar, GameObject> BleedImgRef = AccessTools.FieldRefAccess<HealthBar, GameObject>("BleedImage");
	private static readonly AccessTools.FieldRef<HealthBar, GameObject> BurnImgRef = AccessTools.FieldRefAccess<HealthBar, GameObject>("BurnImage");
	private static readonly AccessTools.FieldRef<HealthBar, GameObject> PoisonImgRef = AccessTools.FieldRefAccess<HealthBar, GameObject>("PoisonImage");
	private static readonly AccessTools.FieldRef<HealthBar, Text> BleedTextRef = AccessTools.FieldRefAccess<HealthBar, Text>("bleedText");
	private static readonly AccessTools.FieldRef<HealthBar, Text> BurnTextRef = AccessTools.FieldRefAccess<HealthBar, Text>("burnText");
	private static readonly AccessTools.FieldRef<HealthBar, Text> PoisonTextRef = AccessTools.FieldRefAccess<HealthBar, Text>("poisonText");
	private static readonly AccessTools.FieldRef<HealthBar, Image> FortLeftRef = AccessTools.FieldRefAccess<HealthBar, Image>("fortImageLeft");
	private static readonly AccessTools.FieldRef<HealthBar, Image> FortRightRef = AccessTools.FieldRefAccess<HealthBar, Image>("fortImageRight");
	private static readonly AccessTools.FieldRef<HealthBar, Image> HasteLeftRef = AccessTools.FieldRefAccess<HealthBar, Image>("hasteImageLeft");
	private static readonly AccessTools.FieldRef<HealthBar, Image> HasteRightRef = AccessTools.FieldRefAccess<HealthBar, Image>("hasteImageRight");

	internal static void Clear()
	{
		States.Clear();
	}

	internal static void OnEnemyRemoved(Enemy enemy)
	{
		if (enemy == null)
		{
			return;
		}
		HealthBar bar = EnemyHealthBarRef(enemy);
		if (bar != null)
		{
			States.Remove(bar.GetInstanceID());
		}
	}

	private static BarState GetState(HealthBar bar)
	{
		int id = bar.GetInstanceID();
		if (!States.TryGetValue(id, out var state))
		{
			state = new BarState();
			States[id] = state;
		}
		return state;
	}

	[HarmonyPatch("SetHealth")]
	[HarmonyPrefix]
	private static void SetHealth_Prefix(HealthBar __instance)
	{
		// Reset state before SetHealth runs so the initial UpdateHealth call initializes all bars and hides the prefab 9999 icons!
		BarState s = GetState(__instance);
		s.LastHeal = -1;
		s.LastArmor = -1;
		s.LastShield = -1;
		s.LastBleeding = -1;
		s.LastBleed = -1;
		s.LastBurning = -1;
		s.LastBurn = -1;
		s.LastPoisoned = -1;
		s.LastPoison = -1;
		s.LastSlow = -1f;
		s.LastBarUpdateTime = 0f;

		GraphicRaycaster gr = __instance.GetComponentInChildren<GraphicRaycaster>();
		if (gr != null)
		{
			gr.enabled = false;
		}
	}

	[HarmonyPatch("Update")]
	[HarmonyPrefix]
	private static bool Update_Prefix(HealthBar __instance)
	{
		if (!Plugin.OptimizeEnemyHealthBars.Value)
		{
			return true;
		}
		ref float dmg = ref DmgRef(__instance);
		float target = HealthRef(__instance) + ArmorRef(__instance) + ShieldRef(__instance);
		if (dmg <= target)
		{
			return false;
		}
		// When many enemies are alive or at 2x/3x speed, snap dmgBar immediately instead of rebuilding World-Space Canvas every frame!
		int enemyCount = (SpawnManager.instance != null) ? SpawnManager.instance.currentEnemies.Count : 0;
		if (enemyCount > 20 || Time.timeScale > 1.5f)
		{
			dmg = target;
			DmgBarImgRef(__instance).rectTransform.sizeDelta = new Vector2(dmg, 0.25f);
			return false;
		}
		return true;
	}

	[HarmonyPatch("UpdateBleed")]
	[HarmonyPrefix]
	private static bool UpdateBleed_Prefix(HealthBar __instance, bool status, int amt)
	{
		if (!Plugin.OptimizeEnemyHealthBars.Value)
		{
			return true;
		}
		BarState s = GetState(__instance);
		int statusInt = status ? 1 : 0;
		if (s.LastBleeding != statusInt)
		{
			s.LastBleeding = statusInt;
			BleedImgRef(__instance).SetActive(status);
			BleedTextRef(__instance).gameObject.SetActive(status);
		}
		if (status && s.LastBleed != amt)
		{
			s.LastBleed = amt;
			BleedTextRef(__instance).text = amt.ToString();
		}
		return false;
	}

	[HarmonyPatch("UpdateBurn")]
	[HarmonyPrefix]
	private static bool UpdateBurn_Prefix(HealthBar __instance, bool status, int amt)
	{
		if (!Plugin.OptimizeEnemyHealthBars.Value)
		{
			return true;
		}
		BarState s = GetState(__instance);
		int statusInt = status ? 1 : 0;
		if (s.LastBurning != statusInt)
		{
			s.LastBurning = statusInt;
			BurnImgRef(__instance).SetActive(status);
			BurnTextRef(__instance).gameObject.SetActive(status);
		}
		if (status && s.LastBurn != amt)
		{
			s.LastBurn = amt;
			BurnTextRef(__instance).text = amt.ToString();
		}
		return false;
	}

	[HarmonyPatch("UpdatePoison")]
	[HarmonyPrefix]
	private static bool UpdatePoison_Prefix(HealthBar __instance, bool status, int amt)
	{
		if (!Plugin.OptimizeEnemyHealthBars.Value)
		{
			return true;
		}
		BarState s = GetState(__instance);
		int statusInt = status ? 1 : 0;
		if (s.LastPoisoned != statusInt)
		{
			s.LastPoisoned = statusInt;
			PoisonImgRef(__instance).SetActive(status);
			PoisonTextRef(__instance).gameObject.SetActive(status);
		}
		if (status && s.LastPoison != amt)
		{
			s.LastPoison = amt;
			PoisonTextRef(__instance).text = amt.ToString();
		}
		return false;
	}

	[HarmonyPatch("UpdateSlow")]
	[HarmonyPrefix]
	private static bool UpdateSlow_Prefix(HealthBar __instance, float currentSlow)
	{
		if (!Plugin.OptimizeEnemyHealthBars.Value)
		{
			return true;
		}
		BarState s = GetState(__instance);
		if (Mathf.Abs(s.LastSlow - currentSlow) >= 0.01f)
		{
			s.LastSlow = currentSlow;
			SlowLeftRef(__instance).fillAmount = currentSlow;
			SlowRightRef(__instance).fillAmount = currentSlow;
		}
		return false;
	}

	[HarmonyPatch("UpdateFortified")]
	[HarmonyPrefix]
	private static bool UpdateFortified_Prefix(HealthBar __instance, float fortTime)
	{
		if (!Plugin.OptimizeEnemyHealthBars.Value)
		{
			return true;
		}
		BarState s = GetState(__instance);
		if (Mathf.Abs(s.LastFort - fortTime) >= 0.05f)
		{
			s.LastFort = fortTime;
			float fill = fortTime * 0.083333f;
			FortLeftRef(__instance).fillAmount = fill;
			FortRightRef(__instance).fillAmount = fill;
		}
		return false;
	}

	[HarmonyPatch("UpdateHaste")]
	[HarmonyPrefix]
	private static bool UpdateHaste_Prefix(HealthBar __instance, float hastePercentage)
	{
		if (!Plugin.OptimizeEnemyHealthBars.Value)
		{
			return true;
		}
		BarState s = GetState(__instance);
		if (Mathf.Abs(s.LastHaste - hastePercentage) >= 0.01f)
		{
			s.LastHaste = hastePercentage;
			HasteLeftRef(__instance).fillAmount = hastePercentage;
			HasteRightRef(__instance).fillAmount = hastePercentage;
		}
		return false;
	}

	[HarmonyPatch("UpdateHealth")]
	[HarmonyPrefix]
	private static bool UpdateHealth_Prefix(
		HealthBar __instance,
		int heal,
		int armr,
		int shld,
		float currentSlow,
		bool isBleeding,
		bool isBurning,
		bool isPoisoned,
		int currentBleed,
		int currentBurn,
		int currentPoison)
	{
		if (!Plugin.OptimizeEnemyHealthBars.Value)
		{
			return true;
		}
		BarState s = GetState(__instance);
		float now = Time.unscaledTime;
		bool isFirstOrLethal = (s.LastHeal == -1 || heal <= 0);

		if (isFirstOrLethal || ((s.LastHeal != heal || s.LastArmor != armr || s.LastShield != shld) && now - s.LastBarUpdateTime >= 0.06f))
		{
			s.LastBarUpdateTime = now;
			s.LastHeal = heal;
			s.LastArmor = armr;
			s.LastShield = shld;
			float scaler = BarScalerRef(__instance);
			float h = (float)heal / scaler;
			float a = (float)armr / scaler;
			float sh = (float)shld / scaler;
			HealthRef(__instance) = h;
			ArmorRef(__instance) = a;
			ShieldRef(__instance) = sh;
			HealthBarImgRef(__instance).rectTransform.sizeDelta = new Vector2(h, 0.25f);
			ArmorBarImgRef(__instance).rectTransform.sizeDelta = new Vector2(a, 0.25f);
			ShieldBarImgRef(__instance).rectTransform.sizeDelta = new Vector2(sh, 0.25f);
			float halfMaskWidth = MaskBarRef(__instance).rectTransform.sizeDelta.x * 0.5f;
			ArmorBarImgRef(__instance).rectTransform.localPosition = new Vector3(h - halfMaskWidth, 0f, 0f);
			ShieldBarImgRef(__instance).rectTransform.localPosition = new Vector3(h + a - halfMaskWidth, 0f, 0f);
		}

		UpdateSlow_Prefix(__instance, currentSlow);
		UpdateBleed_Prefix(__instance, isBleeding, currentBleed);
		UpdateBurn_Prefix(__instance, isBurning, currentBurn);
		UpdatePoison_Prefix(__instance, isPoisoned, currentPoison);
		return false;
	}
}

[HarmonyPatch]
internal static class DamageNumberOptimizer
{
	internal sealed class ActiveNumberEntry
	{
		public DamageNumber Instance;
		public Transform Holder;
		public Text TextComp;
		public Vector3 DriftDir;
		public float Elapsed;
		public float ZoomDuration;
		public float HoldDuration;
		public float SpinDuration;
		public int SpinSign;
		public int OwnerEnemyId;
		public int AccumulatedDamage;
		public float BestFontScale;
		public string BestCritSuffix;
		public string BestColor;
		public float LastTextUpdateTime;
		public int PendingDamage;
	}

	private static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> PooledListRef =
		AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("pooledDamageNumbers");
	private static readonly AccessTools.FieldRef<ObjectPool, GameObject> PrefabRef =
		AccessTools.FieldRefAccess<ObjectPool, GameObject>("damageNumberPrefab");

	private static readonly AccessTools.FieldRef<DamageNumber, Transform> HolderRef =
		AccessTools.FieldRefAccess<DamageNumber, Transform>("holder");
	private static readonly AccessTools.FieldRef<DamageNumber, Text> TextRef =
		AccessTools.FieldRefAccess<DamageNumber, Text>("text");
	private static readonly AccessTools.FieldRef<DamageNumber, float> HoldRef =
		AccessTools.FieldRefAccess<DamageNumber, float>("hold");

	private static readonly List<ActiveNumberEntry> ActiveNumbers = new List<ActiveNumberEntry>(64);
	private static readonly Dictionary<DamageNumber, ActiveNumberEntry> ByDamageNumber = new Dictionary<DamageNumber, ActiveNumberEntry>(64);
	private static readonly Dictionary<int, ActiveNumberEntry> ByEnemyId = new Dictionary<int, ActiveNumberEntry>(256);

	internal static int CurrentDamagedEnemyId;
	internal static bool InTakeDamageContext;

	internal static void Clear()
	{
		ActiveNumbers.Clear();
		ByDamageNumber.Clear();
		ByEnemyId.Clear();
		CurrentDamagedEnemyId = 0;
		InTakeDamageContext = false;
	}

	internal static void OnEnemyRemoved(Enemy enemy)
	{
		if (enemy != null)
		{
			ByEnemyId.Remove(enemy.GetInstanceID());
		}
	}

	// Update all active DamageNumbers in a single centralized loop without coroutines and WITHOUT modifying text.rectTransform.eulerAngles (which forced a World-Space Canvas mesh rebuild every frame!)
	internal static void TickActiveNumbers()
	{
		if (!Plugin.ThrottleDamageNumbers.Value || ActiveNumbers.Count == 0)
		{
			return;
		}
		float dt = Time.deltaTime;
		float now = Time.unscaledTime;
		bool firstPerson = CameraController.instance != null && CameraController.instance.firstPersonMode;

		for (int i = ActiveNumbers.Count - 1; i >= 0; i--)
		{
			ActiveNumberEntry entry = ActiveNumbers[i];
			if (entry.Instance == null || !entry.Instance.gameObject.activeSelf)
			{
				if (entry.OwnerEnemyId != 0 && ByEnemyId.TryGetValue(entry.OwnerEnemyId, out var cur) && cur == entry)
				{
					ByEnemyId.Remove(entry.OwnerEnemyId);
				}
				if (entry.Instance != null)
				{
					ByDamageNumber.Remove(entry.Instance);
				}
				ActiveNumbers.RemoveAt(i);
				continue;
			}

			// Flush any coalesced damage text updates at most 12 times/sec
			if (entry.PendingDamage > 0 && now - entry.LastTextUpdateTime >= 0.08f)
			{
				entry.AccumulatedDamage += entry.PendingDamage;
				entry.PendingDamage = 0;
				entry.LastTextUpdateTime = now;
				entry.Instance.SetText(entry.AccumulatedDamage + entry.BestCritSuffix, entry.BestColor, entry.BestFontScale);
			}

			entry.Elapsed += dt;
			float t = entry.Elapsed;
			float zEnd = entry.ZoomDuration;
			float hEnd = zEnd + entry.HoldDuration;
			float sEnd = hEnd + entry.SpinDuration;

			if (t < zEnd)
			{
				float s = Mathf.Clamp01(t / zEnd);
				entry.Holder.localScale = new Vector3(s, s, s);
			}
			else if (t < hEnd)
			{
				entry.Holder.localScale = Vector3.one;
				entry.Holder.localPosition += entry.DriftDir * dt;
			}
			else if (t < sEnd)
			{
				// Detach from enemy coalescing once spin-out begins
				if (entry.OwnerEnemyId != 0)
				{
					if (ByEnemyId.TryGetValue(entry.OwnerEnemyId, out var cur) && cur == entry)
					{
						ByEnemyId.Remove(entry.OwnerEnemyId);
					}
					entry.OwnerEnemyId = 0;
				}
				float spinProgress = (t - hEnd) / entry.SpinDuration;
				float s = Mathf.Max(0f, 1f - spinProgress);
				entry.Holder.localPosition += entry.DriftDir * dt;
				entry.Holder.localScale = new Vector3(s, s, s);
				// Rotate Holder (3D Transform outside the Canvas) instead of text.rectTransform so Unity NEVER rebuilds the UI Canvas mesh!
				if (!firstPerson)
				{
					entry.Holder.localEulerAngles = new Vector3(0f, 0f, entry.SpinSign * 180f * spinProgress);
				}
			}
			else
			{
				if (entry.OwnerEnemyId != 0 && ByEnemyId.TryGetValue(entry.OwnerEnemyId, out var cur) && cur == entry)
				{
					ByEnemyId.Remove(entry.OwnerEnemyId);
				}
				ByDamageNumber.Remove(entry.Instance);
				ActiveNumbers.RemoveAt(i);
				entry.Holder.localEulerAngles = Vector3.zero;
				ObjectPool.instance?.PoolDamageNumber(entry.Instance.gameObject);
			}
		}
	}

	[HarmonyPatch(typeof(DamageNumber), "Start")]
	[HarmonyPrefix]
	private static bool DamageNumber_Start_Prefix(DamageNumber __instance)
	{
		if (!Plugin.ThrottleDamageNumbers.Value)
		{
			return true;
		}
		Transform holder = HolderRef(__instance);
		Text txt = TextRef(__instance);
		if (txt != null)
		{
			txt.raycastTarget = false;
			txt.rectTransform.localRotation = Quaternion.identity;
		}
		holder.localScale = Vector3.zero;
		holder.localEulerAngles = Vector3.zero;
		if (CameraController.instance == null || !CameraController.instance.firstPersonMode)
		{
			holder.localPosition = new Vector3(-1.5f, 2.12132f, 1.5f);
		}

		if (!ByDamageNumber.TryGetValue(__instance, out var entry))
		{
			entry = new ActiveNumberEntry
			{
				Instance = __instance,
				Holder = holder,
				TextComp = txt
			};
			ByDamageNumber[__instance] = entry;
			ActiveNumbers.Add(entry);
		}

		bool fast = ActiveNumbers.Count > 15 || Time.timeScale > 1.5f;
		entry.Elapsed = 0f;
		entry.ZoomDuration = fast ? 0.15f : 0.22f;
		entry.HoldDuration = fast ? 0.20f : 0.30f;
		entry.SpinDuration = fast ? 0.25f : 0.35f;
		entry.DriftDir = new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(0.2f, 1f), 0f);
		entry.SpinSign = (UnityEngine.Random.Range(0, 2) == 0) ? 1 : -1;
		entry.PendingDamage = 0;
		entry.LastTextUpdateTime = Time.unscaledTime;

		if (InTakeDamageContext && CurrentDamagedEnemyId != 0)
		{
			entry.OwnerEnemyId = CurrentDamagedEnemyId;
			ByEnemyId[CurrentDamagedEnemyId] = entry;
		}
		else
		{
			entry.OwnerEnemyId = 0;
		}

		// Skip starting the vanilla Bloop() coroutine!
		return false;
	}

	[HarmonyPatch(typeof(DamageNumber), "SetHoldTime")]
	[HarmonyPostfix]
	private static void DamageNumber_SetHoldTime_Postfix(DamageNumber __instance, float time)
	{
		if (Plugin.ThrottleDamageNumbers.Value && ByDamageNumber.TryGetValue(__instance, out var entry))
		{
			bool fast = ActiveNumbers.Count > 15 || Time.timeScale > 1.5f;
			entry.HoldDuration = fast ? Mathf.Min(time, 0.22f) : time;
		}
	}

	[HarmonyPatch(typeof(DamageNumber), "SetText")]
	[HarmonyPostfix]
	private static void DamageNumber_SetText_Postfix(DamageNumber __instance, string _text, string color, float fontScale)
	{
		if (!Plugin.ThrottleDamageNumbers.Value || !InTakeDamageContext)
		{
			return;
		}
		if (ByDamageNumber.TryGetValue(__instance, out var entry))
		{
			// Parse leading integer damage from e.g. "125!!"
			int dmg = 0;
			int idx = 0;
			while (idx < _text.Length && _text[idx] >= '0' && _text[idx] <= '9')
			{
				dmg = dmg * 10 + (_text[idx] - '0');
				idx++;
			}
			entry.AccumulatedDamage = dmg;
			entry.BestCritSuffix = (idx < _text.Length) ? _text.Substring(idx) : "";
			entry.BestColor = color;
			entry.BestFontScale = fontScale;
		}
	}

	[HarmonyPatch(typeof(ObjectPool), "SpawnDamageNumber")]
	[HarmonyPrefix]
	private static bool SpawnDamageNumber_Prefix(ObjectPool __instance, Vector3 position, Quaternion rotation, ref GameObject __result)
	{
		if (!Plugin.ThrottleDamageNumbers.Value)
		{
			return true;
		}
		List<GameObject> pool = PooledListRef(__instance);
		while (pool.Count > 0)
		{
			int lastIdx = pool.Count - 1;
			GameObject candidate = pool[lastIdx];
			pool.RemoveAt(lastIdx);
			if (candidate != null)
			{
				candidate.SetActive(value: true);
				candidate.transform.position = position;
				candidate.transform.rotation = rotation;
				candidate.GetComponent<DamageNumber>().Start();
				__result = candidate;
				return false;
			}
		}
		__result = UnityEngine.Object.Instantiate(PrefabRef(__instance), position, rotation);
		return false;
	}

	internal struct TakeDamageState
	{
		public bool EnteredContext;
		public bool ChangedShowFlag;
		public bool PrevShowFlag;
		public int PrevEnemyId;
		public bool PrevContext;
		public int PreHealth;
		public int PreArmor;
		public int PreShield;
		public ActiveNumberEntry CoalesceTarget;
		public float CritChance;
	}

	[HarmonyPatch(typeof(Enemy), "TakeDamage")]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool Enemy_TakeDamage_Prefix(Enemy __instance, TowerType whoHitMe, int _baseDmg, float critChance, out TakeDamageState __state)
	{
		__state = default;
		if (__instance != null && (Plugin.OptimizeTargetingAndEnemyLookups.Value || Plugin.ThrottleDamageNumbers.Value) && EnemyAndTowerTargetingPatches.EnemyDeadRef(__instance))
		{
			return false;
		}
		if (!Plugin.ThrottleDamageNumbers.Value || OptionsMenu.instance == null || !OptionsMenu.instance.showDamageNumbers)
		{
			return true;
		}

		__state.EnteredContext = true;
		__state.PrevEnemyId = CurrentDamagedEnemyId;
		__state.PrevContext = InTakeDamageContext;
		int enemyId = __instance.GetInstanceID();
		CurrentDamagedEnemyId = enemyId;
		InTakeDamageContext = true;

		int maxAllowed = (Time.timeScale > 1.5f) ? Mathf.Max(15, Plugin.MaxActiveDamageNumbers.Value * 2 / 3) : Plugin.MaxActiveDamageNumbers.Value;

		// Check if this enemy already has an active floating DamageNumber we can accumulate into!
		if (ByEnemyId.TryGetValue(enemyId, out var existing) &&
			existing.Instance != null &&
			existing.Instance.gameObject.activeSelf &&
			existing.Elapsed < existing.ZoomDuration + existing.HoldDuration)
		{
			__state.ChangedShowFlag = true;
			__state.PrevShowFlag = OptionsMenu.instance.showDamageNumbers;
			OptionsMenu.instance.showDamageNumbers = false;
			__state.CoalesceTarget = existing;
			__state.PreHealth = __instance.health;
			__state.PreArmor = __instance.armor;
			__state.PreShield = __instance.shield;
			__state.CritChance = critChance;
			return true;
		}

		// Suppress routine DOT tick popups (_baseDmg == -999) when screen already has several active numbers or during 2x/3x speed
		if ((_baseDmg == -999 || whoHitMe == TowerType.DOT) && (ActiveNumbers.Count >= 10 || Time.timeScale > 1.5f))
		{
			__state.ChangedShowFlag = true;
			__state.PrevShowFlag = OptionsMenu.instance.showDamageNumbers;
			OptionsMenu.instance.showDamageNumbers = false;
			return true;
		}

		// Hard cap simultaneous World-Space DamageNumber Canvases
		if (ActiveNumbers.Count >= maxAllowed)
		{
			__state.ChangedShowFlag = true;
			__state.PrevShowFlag = OptionsMenu.instance.showDamageNumbers;
			OptionsMenu.instance.showDamageNumbers = false;
		}
		return true;
	}

	[HarmonyPatch(typeof(Enemy), "TakeDamage")]
	[HarmonyPostfix]
	private static void Enemy_TakeDamage_Postfix(Enemy __instance, TakeDamageState __state)
	{
		if (!__state.EnteredContext || !Plugin.ThrottleDamageNumbers.Value)
		{
			return;
		}
		CurrentDamagedEnemyId = __state.PrevEnemyId;
		InTakeDamageContext = __state.PrevContext;

		if (__state.ChangedShowFlag && OptionsMenu.instance != null)
		{
			OptionsMenu.instance.showDamageNumbers = __state.PrevShowFlag;
		}

		ActiveNumberEntry target = __state.CoalesceTarget;
		if (target != null && target.Instance != null && target.Instance.gameObject.activeSelf)
		{
			int delta = (__state.PreHealth - __instance.health) + (__state.PreArmor - __instance.armor) + (__state.PreShield - __instance.shield);
			if (delta > 0)
			{
				target.PendingDamage += delta;
				target.Instance.transform.position = __instance.transform.position;
				// Slightly extend hold time so the combined number stays readable
				target.Elapsed = Mathf.Min(target.Elapsed, target.ZoomDuration + target.HoldDuration * 0.4f);
			}
		}
	}
}

[HarmonyPatch]
internal static class ParticleAndFXOptimizer
{
	private static readonly HashSet<ProjectileFX> ActiveFXSet = new HashSet<ProjectileFX>();
	private static readonly HashSet<Landmine> ActiveLandmines = new HashSet<Landmine>();
	private static int _activeExplosions;

	private static readonly AccessTools.FieldRef<ProjectileFX, GameObject> BleedingPSRef =
		AccessTools.FieldRefAccess<ProjectileFX, GameObject>("bleedingPS");
	private static readonly AccessTools.FieldRef<ProjectileFX, GameObject> BurningPSRef =
		AccessTools.FieldRefAccess<ProjectileFX, GameObject>("burrningPS");
	private static readonly AccessTools.FieldRef<ProjectileFX, GameObject> PoisonPSRef =
		AccessTools.FieldRefAccess<ProjectileFX, GameObject>("poisonPS");

	private static readonly AccessTools.FieldRef<Explosion, float> ExplosionDurationRef =
		AccessTools.FieldRefAccess<Explosion, float>("duration");

	internal static void Clear()
	{
		ActiveFXSet.Clear();
		ActiveLandmines.Clear();
		_activeExplosions = 0;
	}

	private static bool ShouldSpawnProjectileFX(Tower tower)
	{
		if (!Plugin.OptimizeProjectileAndExplosionFX.Value)
		{
			return true;
		}
		if (OptionsMenu.instance != null && !OptionsMenu.instance.extraProjectileEffects)
		{
			return false;
		}
		if (tower.bleedPercent <= 0f && tower.burnPercent <= 0f && tower.poisonPercent <= 0f && tower.slowPercent <= 0f && !tower.consumesMana)
		{
			return false;
		}
		// Prune destroyed ProjectileFX instances (e.g. projectiles that expired via maximumLifeTime without calling OnDetach)
		ActiveFXSet.RemoveWhere(fx => fx == null);
		int cap = (Time.timeScale > 1.5f) ? Mathf.Max(8, Plugin.MaxActiveProjectileFX.Value / 2) : Plugin.MaxActiveProjectileFX.Value;
		return ActiveFXSet.Count < cap;
	}

	[HarmonyPatch(typeof(Tower), "Fire")]
	[HarmonyPrefix]
	private static void Tower_Fire_Prefix(Tower __instance, ref GameObject ___extraProjectileFX, out GameObject __state)
	{
		__state = ___extraProjectileFX;
		if (___extraProjectileFX != null && !ShouldSpawnProjectileFX(__instance))
		{
			___extraProjectileFX = null;
		}
	}

	[HarmonyPatch(typeof(Tower), "Fire")]
	[HarmonyPostfix]
	private static void Tower_Fire_Postfix(ref GameObject ___extraProjectileFX, GameObject __state)
	{
		if (__state != null)
		{
			___extraProjectileFX = __state;
		}
	}

	[HarmonyPatch(typeof(Cannon), "Fire")]
	[HarmonyPrefix]
	private static void Cannon_Fire_Prefix(Cannon __instance, ref GameObject ___extraProjectileFX, out GameObject __state)
	{
		__state = ___extraProjectileFX;
		if (___extraProjectileFX != null && !ShouldSpawnProjectileFX(__instance))
		{
			___extraProjectileFX = null;
		}
	}

	[HarmonyPatch(typeof(Cannon), "Fire")]
	[HarmonyPostfix]
	private static void Cannon_Fire_Postfix(ref GameObject ___extraProjectileFX, GameObject __state)
	{
		if (__state != null)
		{
			___extraProjectileFX = __state;
		}
	}

	[HarmonyPatch(typeof(Morter), "Fire")]
	[HarmonyPrefix]
	private static void Morter_Fire_Prefix(Morter __instance, ref GameObject ___extraProjectileFX, out GameObject __state)
	{
		__state = ___extraProjectileFX;
		if (___extraProjectileFX != null && !ShouldSpawnProjectileFX(__instance))
		{
			___extraProjectileFX = null;
		}
	}

	[HarmonyPatch(typeof(Morter), "Fire")]
	[HarmonyPostfix]
	private static void Morter_Fire_Postfix(ref GameObject ___extraProjectileFX, GameObject __state)
	{
		if (__state != null)
		{
			___extraProjectileFX = __state;
		}
	}

	[HarmonyPatch(typeof(Monument), "Fire")]
	[HarmonyPrefix]
	private static void Monument_Fire_Prefix(Monument __instance, ref GameObject ___extraProjectileFX, out GameObject __state)
	{
		__state = ___extraProjectileFX;
		if (___extraProjectileFX != null && !ShouldSpawnProjectileFX(__instance))
		{
			___extraProjectileFX = null;
		}
	}

	[HarmonyPatch(typeof(Monument), "Fire")]
	[HarmonyPostfix]
	private static void Monument_Fire_Postfix(ref GameObject ___extraProjectileFX, GameObject __state)
	{
		if (__state != null)
		{
			___extraProjectileFX = __state;
		}
	}

	[HarmonyPatch(typeof(ProjectileFX), "SetFX")]
	[HarmonyPostfix]
	private static void ProjectileFX_SetFX_Postfix(ProjectileFX __instance)
	{
		if (__instance != null)
		{
			ActiveFXSet.Add(__instance);
		}
	}

	[HarmonyPatch(typeof(ProjectileFX), "OnDetach")]
	[HarmonyPrefix]
	private static bool ProjectileFX_OnDetach_Prefix(ProjectileFX __instance)
	{
		if (!Plugin.OptimizeProjectileAndExplosionFX.Value)
		{
			return true;
		}
		__instance.StartCoroutine(FastDie(__instance));
		return false;
	}

	private static IEnumerator FastDie(ProjectileFX fx)
	{
		GameObject bleed = BleedingPSRef(fx);
		GameObject burn = BurningPSRef(fx);
		GameObject poison = PoisonPSRef(fx);
		if (bleed != null && bleed.activeSelf) bleed.GetComponent<ParticleSystem>()?.Stop(withChildren: true);
		if (burn != null && burn.activeSelf) burn.GetComponent<ParticleSystem>()?.Stop(withChildren: true);
		if (poison != null && poison.activeSelf) poison.GetComponent<ParticleSystem>()?.Stop(withChildren: true);
		yield return new WaitForSeconds(0.25f);
		ActiveFXSet.Remove(fx);
		if (fx != null)
		{
			UnityEngine.Object.Destroy(fx.gameObject);
		}
	}

	[HarmonyPatch(typeof(Explosion), "Start")]
	[HarmonyPrefix]
	private static bool Explosion_Start_Prefix(Explosion __instance)
	{
		if (!Plugin.OptimizeProjectileAndExplosionFX.Value)
		{
			return true;
		}
		int cap = (Time.timeScale > 1.5f) ? Mathf.Max(6, Plugin.MaxActiveExplosions.Value / 2) : Plugin.MaxActiveExplosions.Value;
		if (_activeExplosions >= cap)
		{
			UnityEngine.Object.Destroy(__instance.gameObject);
			return false;
		}
		_activeExplosions++;
		ExplosionDurationRef(__instance) = Mathf.Min(ExplosionDurationRef(__instance), 0.55f);
		return true;
	}

	[HarmonyPatch(typeof(Explosion), "FixedUpdate")]
	[HarmonyPostfix]
	private static void Explosion_FixedUpdate_Postfix(Explosion __instance)
	{
		if (Plugin.OptimizeProjectileAndExplosionFX.Value && ExplosionDurationRef(__instance) <= 0f)
		{
			_activeExplosions = Mathf.Max(0, _activeExplosions - 1);
		}
	}

	[HarmonyPatch(typeof(Landmine), "Start")]
	[HarmonyPostfix]
	private static void Landmine_Start_Postfix(Landmine __instance)
	{
		if (__instance != null)
		{
			ActiveLandmines.Add(__instance);
		}
	}

	[HarmonyPatch(typeof(Landmine), "Explode")]
	[HarmonyPostfix]
	private static void Landmine_Explode_Postfix(Landmine __instance)
	{
		if (__instance != null)
		{
			ActiveLandmines.Remove(__instance);
		}
	}

	[HarmonyPatch(typeof(Encampment), "Fire")]
	[HarmonyPrefix]
	private static bool Encampment_Fire_Prefix()
	{
		if (!Plugin.OptimizeProjectileAndExplosionFX.Value)
		{
			return true;
		}
		if (ActiveLandmines.Count >= 180)
		{
			ActiveLandmines.RemoveWhere(m => m == null);
			if (ActiveLandmines.Count >= 180)
			{
				return false;
			}
		}
		return true;
	}
}

[HarmonyPatch]
internal static class CoroutineLeakPatches
{
	private static readonly AccessTools.FieldRef<Tower, LayerMask> TowerEnemyMaskRef =
		AccessTools.FieldRefAccess<Tower, LayerMask>("enemyLayerMask");
	private static readonly AccessTools.FieldRef<TeslaCoil, GameObject> LightningTrailRef =
		AccessTools.FieldRefAccess<TeslaCoil, GameObject>("lightningTrail");
	private static readonly AccessTools.FieldRef<VampireLair, GameObject[]> BloodTrailsRef =
		AccessTools.FieldRefAccess<VampireLair, GameObject[]>("bloodTrails");

	private static readonly List<Enemy> TempEnemySnapshot = new List<Enemy>(128);
	private static int _activeTrailAnimations;

	internal static void Clear()
	{
		_activeTrailAnimations = 0;
		TempEnemySnapshot.Clear();
	}

	private static void DealAoEDamageInRange(Tower tower, Vector3 centerPos, LayerMask enemyLayerMask)
	{
		HashSet<Enemy> liveEnemies = SpawnManager.instance != null ? SpawnManager.instance.currentEnemies : null;
		if (Plugin.OptimizeTargetingAndEnemyLookups.Value && liveEnemies != null && (tower.range >= 35f || liveEnemies.Count <= 64))
		{
			if (liveEnemies.Count == 0)
			{
				return;
			}
			float maxDist = tower.range + 0.5f;
			float maxDistSq = maxDist * maxDist;
			TempEnemySnapshot.Clear();
			foreach (Enemy e in liveEnemies)
			{
				if (e != null)
				{
					TempEnemySnapshot.Add(e);
				}
			}
			for (int i = 0; i < TempEnemySnapshot.Count; i++)
			{
				Enemy enemy = TempEnemySnapshot[i];
				if (enemy == null || EnemyAndTowerTargetingPatches.EnemyDeadRef(enemy)) continue;
				Vector3 ep = enemy.transform.position;
				float dx = ep.x - centerPos.x;
				float dy = ep.y - centerPos.y;
				float dz = ep.z - centerPos.z;
				if (dx * dx + dy * dy + dz * dz <= maxDistSq)
				{
					enemy.TakeDamage(tower.towerType, tower.damage, tower.healthDamage, tower.armorDamage, tower.shieldDamage, tower.slowPercent, tower.bleedPercent, tower.burnPercent, tower.poisonPercent, tower.critChance, tower.stunChance);
				}
			}
			TempEnemySnapshot.Clear();
			return;
		}

		Collider[] array = Physics.OverlapSphere(centerPos, tower.range, enemyLayerMask, QueryTriggerInteraction.Collide);
		for (int i = 0; i < array.Length; i++)
		{
			if (EnemyComponentCache.TryGetByCollider(array[i], out var cached))
			{
				cached.Enemy.TakeDamage(tower.towerType, tower.damage, tower.healthDamage, tower.armorDamage, tower.shieldDamage, tower.slowPercent, tower.bleedPercent, tower.burnPercent, tower.poisonPercent, tower.critChance, tower.stunChance);
			}
			else
			{
				array[i].GetComponent<IDamageable>()?.TakeDamage(tower.towerType, tower.damage, tower.healthDamage, tower.armorDamage, tower.shieldDamage, tower.slowPercent, tower.bleedPercent, tower.burnPercent, tower.poisonPercent, tower.critChance, tower.stunChance);
			}
		}
	}

	[HarmonyPatch(typeof(TeslaCoil), "Fire")]
	[HarmonyPrefix]
	private static bool TeslaCoil_Fire_Prefix(TeslaCoil __instance)
	{
		if (!Plugin.FixTeslaAndVampireTrailCoroutines.Value)
		{
			return true;
		}
		if (__instance.consumesMana)
		{
			int manaCost = (int)((float)__instance.damage * __instance.manaConsumptionRate);
			if (!ResourceManager.instance.CheckMana(manaCost))
			{
				return false;
			}
			ResourceManager.instance.SpendMana(manaCost);
		}
		Vector3 position = __instance.transform.position;
		position.y = 0f;
		DealAoEDamageInRange(__instance, position, TowerEnemyMaskRef(__instance));
		SFXManager.instance.PlaySound(Sound.TeslaZap, __instance.transform.position);
		if (_activeTrailAnimations < 8)
		{
			__instance.StopAllCoroutines();
			__instance.StartCoroutine(DrawLightningFast(__instance));
		}
		return false;
	}

	private static IEnumerator DrawLightningFast(TeslaCoil coil)
	{
		_activeTrailAnimations++;
		try
		{
			GameObject trail = LightningTrailRef(coil);
			Vector3 basePos = coil.transform.position;
			float range = Mathf.Min(coil.range, 25f);
			for (float i = 0f; i <= (float)Math.PI * 2f; i += 0.8f)
			{
				if (trail == null) break;
				Vector3 jitter = new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), UnityEngine.Random.Range(-0.5f, 0.5f), UnityEngine.Random.Range(-0.5f, 0.5f));
				trail.transform.position = jitter + new Vector3(basePos.x + Mathf.Sin(i) * range, 0.75f, basePos.z + Mathf.Cos(i) * range);
				// yield return null instead of new WaitForEndOfFrame() (zero GC allocation!)
				yield return null;
			}
		}
		finally
		{
			_activeTrailAnimations = Mathf.Max(0, _activeTrailAnimations - 1);
		}
	}

	[HarmonyPatch(typeof(VampireLair), "Fire")]
	[HarmonyPrefix]
	private static bool VampireLair_Fire_Prefix(VampireLair __instance)
	{
		if (!Plugin.FixTeslaAndVampireTrailCoroutines.Value)
		{
			return true;
		}
		if (__instance.consumesMana)
		{
			int manaCost = (int)((float)__instance.damage * __instance.manaConsumptionRate);
			if (!ResourceManager.instance.CheckMana(manaCost))
			{
				return false;
			}
			ResourceManager.instance.SpendMana(manaCost);
		}
		Vector3 position = __instance.transform.position;
		position.y = 0f;
		DealAoEDamageInRange(__instance, position, TowerEnemyMaskRef(__instance));
		if (_activeTrailAnimations < 8)
		{
			__instance.StopAllCoroutines();
			GameObject[] trails = BloodTrailsRef(__instance);
			if (trails != null && trails.Length > 0)
			{
				__instance.StartCoroutine(DrawBloodFast(__instance, trails));
			}
		}
		return false;
	}

	private static IEnumerator DrawBloodFast(VampireLair lair, GameObject[] trails)
	{
		_activeTrailAnimations++;
		try
		{
			float startValue = UnityEngine.Random.Range(-(float)Math.PI, (float)Math.PI);
			Vector3 basePos = lair.transform.position;
			float range = Mathf.Min(lair.range, 25f);
			for (float i = 0f; i <= (float)Math.PI; i += Time.deltaTime * 2f)
			{
				float factor = range * Mathf.Max(((float)Math.PI - i) / (float)Math.PI, 0f);
				for (int j = 0; j < trails.Length; j++)
				{
					GameObject t = trails[j];
					if (t != null)
					{
						float angle = startValue + i + j * 2.094f;
						t.transform.position = new Vector3(basePos.x + Mathf.Sin(angle) * factor, 0.75f, basePos.z + Mathf.Cos(angle) * factor);
					}
				}
				// Single coroutine moves all 3 blood trails with yield return null (zero WaitForEndOfFrame GC allocations!)
				yield return null;
			}
		}
		finally
		{
			_activeTrailAnimations = Mathf.Max(0, _activeTrailAnimations - 1);
		}
	}
}

[HarmonyPatch(typeof(SFXManager))]
internal static class AudioSpamOptimizer
{
	private static readonly float[] LastPlayTimeBySound = new float[32];
	private delegate AudioClip GetClipDelegate(SFXManager instance, Sound s);
	private static readonly GetClipDelegate GetClipFast =
		(GetClipDelegate)Delegate.CreateDelegate(
			typeof(GetClipDelegate),
			typeof(SFXManager).GetMethod("GetClip", BindingFlags.Instance | BindingFlags.NonPublic));
	private static readonly AccessTools.FieldRef<SFXManager, GameObject> SourceObjectRef = AccessTools.FieldRefAccess<SFXManager, GameObject>("sourceObject");

	internal static void Clear()
	{
		for (int i = 0; i < LastPlayTimeBySound.Length; i++)
		{
			LastPlayTimeBySound[i] = 0f;
		}
	}

	[HarmonyPatch("PlaySound", new Type[] { typeof(Sound), typeof(Vector3), typeof(Transform) })]
	[HarmonyPrefix]
	private static bool PlaySound_Prefix(SFXManager __instance, Sound s, Vector3 pos, Transform parent)
	{
		if (!Plugin.DeduplicateCombatAudio.Value)
		{
			return true;
		}
		if (__instance.volume <= 0f || s == Sound.None)
		{
			return false;
		}
		int soundIdx = (int)s;
		bool isUI = (s == Sound.ButtonClick || s == Sound.Cards);
		if (!isUI && soundIdx >= 0 && soundIdx < LastPlayTimeBySound.Length)
		{
			float now = Time.unscaledTime;
			if (now - LastPlayTimeBySound[soundIdx] < Plugin.SameSoundCooldownSeconds.Value)
			{
				return false;
			}
			LastPlayTimeBySound[soundIdx] = now;
		}

		AudioClip clip = GetClipFast(__instance, s);
		if (clip == null)
		{
			return false;
		}

		List<AudioPoolSource> pool = __instance.sources;
		AudioPoolSource audioPoolSource = null;
		while (pool.Count > 0)
		{
			int lastIdx = pool.Count - 1;
			AudioPoolSource candidate = pool[lastIdx];
			pool.RemoveAt(lastIdx);
			if (candidate != null)
			{
				audioPoolSource = candidate;
				break;
			}
		}
		if (audioPoolSource == null)
		{
			audioPoolSource = UnityEngine.Object.Instantiate(SourceObjectRef(__instance)).GetComponent<AudioPoolSource>();
		}
		audioPoolSource.transform.position = pos;
		audioPoolSource.PlayClip(clip, __instance.volume, 0.08333f);
		if (parent != null)
		{
			audioPoolSource.transform.parent = parent;
		}
		return false;
	}
}
