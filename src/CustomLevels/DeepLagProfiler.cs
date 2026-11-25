using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace RogueTowerCustomLevels;

internal static class DeepLagProfiler
{
	private enum Bucket
	{
		TowerDetectEnemies = 0,
		TowerUpdateAndXP = 1,
		TowerFireAll = 2,
		ProjectilesAndAoE = 3,
		EnemyUpdateAndDOT = 4,
		EnemyTakeDamage = 5,
		PathfinderMove = 6,
		WalkAnimator = 7,
		HealthBarWorldUI = 8,
		DamageNumbers = 9,
		CombatAudioSFX = 10,
		HudResourceText = 11,
		DropperPathRaycast = 12,
		Count = 13
	}

	private static readonly string[] BucketNames =
	{
		"Tower.DetectEnemies (OverlapSphere+Select)",
		"Tower.Update + GainXP + Turret",
		"Tower.Fire (All Tower Subclasses)",
		"Projectiles + AoE Hits + Explosions",
		"Enemy.Update (DOT+Speed+Regen)",
		"Enemy.TakeDamage",
		"Pathfinder.FixedUpdate",
		"WalkAnimator.FixedUpdate",
		"HealthBar World-Space UI",
		"DamageNumber Spawn+SetText",
		"SFXManager.PlaySound",
		"ResourceManager HUD Text",
		"Dropper.SetDropPoints (Wave Path Raycast)"
	};

	private sealed class FrameSpike
	{
		public int Frame;
		public double FrameMs;
		public double ScriptsMs;
		public double EngineGcMs;
		public int PhysicsSteps;
		public bool GcTriggered;
		public bool WaveAdvanced;
		public string TopCauses;
	}

	private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;
	private static readonly int TowerTypeCount = 18; // TowerType enum 0..17

	internal static volatile bool Active;

	private static readonly long[] TotalTicks = new long[(int)Bucket.Count];
	private static readonly long[] MaxTicks = new long[(int)Bucket.Count];
	private static readonly long[] FrameTicks = new long[(int)Bucket.Count];
	private static readonly int[] Calls = new int[(int)Bucket.Count];
	private static readonly int[] Depth = new int[(int)Bucket.Count];

	private static readonly long[] TowerTargetTicks = new long[18];
	private static readonly int[] TowerTargetCalls = new int[18];
	private static readonly long[] TowerFireTicks = new long[18];
	private static readonly int[] TowerFireCalls = new int[18];
	private static int _towerFireDepth;

	private static int _renderedFrames;
	private static int _physicsSteps;
	private static int _framePhysicsSteps;
	private static double _totalSampleMs;
	private static int _gc0Start, _gc1Start, _gc2Start;
	private static int _prevGc0, _prevGc1, _prevGc2;
	private static int _prevWave;
	private static int _gc0Delta, _gc1Delta, _gc2Delta;
	private static long _memStartBytes, _memEndBytes;

	private static int _over16Ms, _over33Ms, _over50Ms, _over100Ms;
	private static readonly List<FrameSpike> WorstSpikes = new List<FrameSpike>(6);

	private static bool _hooksInstalled;

	internal static void Initialize(Harmony harmony)
	{
		if (_hooksInstalled || harmony == null)
		{
			return;
		}
		_hooksInstalled = true;

		// Generic bucket hooks
		PatchBucket(harmony, typeof(Tower), "DetectEnemies", (int)Bucket.TowerDetectEnemies, isDetectEnemies: true);

		PatchBucket(harmony, typeof(Tower), "Update", (int)Bucket.TowerUpdateAndXP);
		PatchBucket(harmony, typeof(FlameThrower), "Update", (int)Bucket.TowerUpdateAndXP);
		PatchBucket(harmony, typeof(Dropper), "Update", (int)Bucket.TowerUpdateAndXP);
		PatchBucket(harmony, typeof(Obelisk), "Update", (int)Bucket.TowerUpdateAndXP);
		PatchBucket(harmony, typeof(RadarTower), "Update", (int)Bucket.TowerUpdateAndXP);
		PatchBucket(harmony, typeof(BiPlane), "Update", (int)Bucket.TowerUpdateAndXP);

		PatchBucket(harmony, typeof(Tower), "Fire", (int)Bucket.TowerFireAll, isTowerFire: true);
		PatchBucket(harmony, typeof(Morter), "Fire", (int)Bucket.TowerFireAll, isTowerFire: true);
		PatchBucket(harmony, typeof(FlameThrower), "Fire", (int)Bucket.TowerFireAll, isTowerFire: true);
		PatchBucket(harmony, typeof(Dropper), "Fire", (int)Bucket.TowerFireAll, isTowerFire: true);
		PatchBucket(harmony, typeof(Encampment), "Fire", (int)Bucket.TowerFireAll, isTowerFire: true);
		PatchBucket(harmony, typeof(Cannon), "Fire", (int)Bucket.TowerFireAll, isTowerFire: true);
		PatchBucket(harmony, typeof(TeslaCoil), "Fire", (int)Bucket.TowerFireAll, isTowerFire: true);
		PatchBucket(harmony, typeof(VampireLair), "Fire", (int)Bucket.TowerFireAll, isTowerFire: true);
		PatchBucket(harmony, typeof(Obelisk), "Fire", (int)Bucket.TowerFireAll, isTowerFire: true);
		PatchBucket(harmony, typeof(Monument), "Fire", (int)Bucket.TowerFireAll, isTowerFire: true);

		PatchBucket(harmony, typeof(Projectile), "FixedUpdate", (int)Bucket.ProjectilesAndAoE);
		PatchBucket(harmony, typeof(Projectile), "OnHit", (int)Bucket.ProjectilesAndAoE);
		PatchBucket(harmony, typeof(Cannonball), "FixedUpdate", (int)Bucket.ProjectilesAndAoE);
		PatchBucket(harmony, typeof(Cannonball), "CheckForHits", (int)Bucket.ProjectilesAndAoE);
		PatchBucket(harmony, typeof(Cannonball), "OnHit", (int)Bucket.ProjectilesAndAoE);
		PatchBucket(harmony, typeof(MorterShell), "OnHit", (int)Bucket.ProjectilesAndAoE);
		PatchBucket(harmony, typeof(Sawblade), "CheckForHits", (int)Bucket.ProjectilesAndAoE);
		PatchBucket(harmony, typeof(Landmine), "Explode", (int)Bucket.ProjectilesAndAoE);
		PatchBucket(harmony, typeof(Explosion), "FixedUpdate", (int)Bucket.ProjectilesAndAoE);

		PatchBucket(harmony, typeof(Enemy), "Update", (int)Bucket.EnemyUpdateAndDOT);
		PatchBucket(harmony, typeof(Enemy), "TakeDamage", (int)Bucket.EnemyTakeDamage);
		PatchBucket(harmony, typeof(Pathfinder), "FixedUpdate", (int)Bucket.PathfinderMove);
		PatchBucket(harmony, typeof(WalkAnimator), "FixedUpdate", (int)Bucket.WalkAnimator);

		PatchBucket(harmony, typeof(HealthBar), "Update", (int)Bucket.HealthBarWorldUI);
		PatchBucket(harmony, typeof(HealthBar), "UpdateHealth", (int)Bucket.HealthBarWorldUI);
		PatchBucket(harmony, typeof(HealthBar), "UpdateBleed", (int)Bucket.HealthBarWorldUI);
		PatchBucket(harmony, typeof(HealthBar), "UpdateBurn", (int)Bucket.HealthBarWorldUI);
		PatchBucket(harmony, typeof(HealthBar), "UpdatePoison", (int)Bucket.HealthBarWorldUI);
		PatchBucket(harmony, typeof(HealthBar), "UpdateSlow", (int)Bucket.HealthBarWorldUI);

		PatchBucket(harmony, typeof(ObjectPool), "SpawnDamageNumber", (int)Bucket.DamageNumbers);
		PatchBucket(harmony, typeof(DamageNumber), "SetText", (int)Bucket.DamageNumbers);
		PatchBucket(harmony, typeof(SFXManager), "PlaySound", (int)Bucket.CombatAudioSFX, new[] { typeof(Sound), typeof(Vector3), typeof(Transform) });
		PatchBucket(harmony, typeof(ResourceManager), "UpdateResourceText", (int)Bucket.HudResourceText);
		PatchBucket(harmony, typeof(Dropper), "SetDropPoints", (int)Bucket.DropperPathRaycast);

		// Physics step counter (runs once per FixedUpdate tick)
		MethodInfo bmFixed = AccessTools.DeclaredMethod(typeof(BuildingManager), "FixedUpdate");
		if (bmFixed != null)
		{
			harmony.Patch(bmFixed, postfix: new HarmonyMethod(typeof(DeepLagProfiler), nameof(BuildingManager_FixedUpdate_Postfix)));
		}
	}

	private static void PatchBucket(Harmony harmony, Type targetType, string methodName, int bucket, Type[] paramTypes = null, bool isDetectEnemies = false, bool isTowerFire = false)
	{
		try
		{
			MethodInfo target = paramTypes != null
				? AccessTools.DeclaredMethod(targetType, methodName, paramTypes)
				: AccessTools.DeclaredMethod(targetType, methodName);
			if (target == null)
			{
				return;
			}

			string preName;
			string postName;
			if (isDetectEnemies)
			{
				preName = nameof(DetectEnemies_Prefix);
				postName = nameof(DetectEnemies_Postfix);
			}
			else if (isTowerFire)
			{
				preName = nameof(TowerFire_Prefix);
				postName = nameof(TowerFire_Postfix);
			}
			else
			{
				string suffix = bucket.ToString(CultureInfo.InvariantCulture);
				preName = "Pre_" + suffix;
				postName = "Post_" + suffix;
			}

			MethodInfo pre = typeof(DeepLagProfiler).GetMethod(preName, BindingFlags.Static | BindingFlags.NonPublic);
			MethodInfo post = typeof(DeepLagProfiler).GetMethod(postName, BindingFlags.Static | BindingFlags.NonPublic);
			if (pre == null || post == null)
			{
				return;
			}

			harmony.Patch(
				target,
				prefix: new HarmonyMethod(pre) { priority = Priority.First },
				postfix: new HarmonyMethod(post) { priority = Priority.Last });
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning($"DeepLagProfiler skipped hook {targetType.Name}.{methodName}: {ex.Message}");
		}
	}

	internal static void BeginSession()
	{
		Array.Clear(TotalTicks, 0, TotalTicks.Length);
		Array.Clear(MaxTicks, 0, MaxTicks.Length);
		Array.Clear(FrameTicks, 0, FrameTicks.Length);
		Array.Clear(Calls, 0, Calls.Length);
		Array.Clear(Depth, 0, Depth.Length);

		Array.Clear(TowerTargetTicks, 0, TowerTargetTicks.Length);
		Array.Clear(TowerTargetCalls, 0, TowerTargetCalls.Length);
		Array.Clear(TowerFireTicks, 0, TowerFireTicks.Length);
		Array.Clear(TowerFireCalls, 0, TowerFireCalls.Length);
		_towerFireDepth = 0;

		_renderedFrames = 0;
		_physicsSteps = 0;
		_framePhysicsSteps = 0;
		_totalSampleMs = 0.0;
		_over16Ms = 0;
		_over33Ms = 0;
		_over50Ms = 0;
		_over100Ms = 0;
		WorstSpikes.Clear();

		_gc0Start = _prevGc0 = GC.CollectionCount(0);
		_gc1Start = _prevGc1 = GC.CollectionCount(1);
		_gc2Start = _prevGc2 = GC.CollectionCount(2);
		_prevWave = SpawnManager.instance != null ? SpawnManager.instance.level : -1;
		_memStartBytes = GC.GetTotalMemory(false);

		Active = true;
	}

	internal static void RecordRenderedFrame(float unscaledDt)
	{
		if (!Active)
		{
			return;
		}
		_renderedFrames++;
		double frameMs = unscaledDt * 1000.0;
		_totalSampleMs += frameMs;

		if (frameMs > 16.67) _over16Ms++;
		if (frameMs > 33.33) _over33Ms++;
		if (frameMs > 50.0) _over50Ms++;
		if (frameMs > 100.0) _over100Ms++;

		int gc0 = GC.CollectionCount(0);
		int gc1 = GC.CollectionCount(1);
		int gc2 = GC.CollectionCount(2);
		bool gcRan = (gc0 > _prevGc0) || (gc1 > _prevGc1) || (gc2 > _prevGc2);
		_prevGc0 = gc0;
		_prevGc1 = gc1;
		_prevGc2 = gc2;

		int curWave = SpawnManager.instance != null ? SpawnManager.instance.level : _prevWave;
		bool waveAdvanced = curWave != _prevWave;
		_prevWave = curWave;

		if (WorstSpikes.Count < 5 || frameMs > WorstSpikes[WorstSpikes.Count - 1].FrameMs)
		{
			double scriptsMs =
				(FrameTicks[(int)Bucket.TowerDetectEnemies] +
				 FrameTicks[(int)Bucket.TowerUpdateAndXP] +
				 FrameTicks[(int)Bucket.TowerFireAll] +
				 FrameTicks[(int)Bucket.ProjectilesAndAoE] +
				 FrameTicks[(int)Bucket.EnemyUpdateAndDOT] +
				 FrameTicks[(int)Bucket.PathfinderMove] +
				 FrameTicks[(int)Bucket.WalkAnimator] +
				 FrameTicks[(int)Bucket.DropperPathRaycast]) * TicksToMs;
			double engineGcMs = Math.Max(0.0, frameMs - scriptsMs);

			int b1 = -1, b2 = -1, b3 = -1;
			long m1 = 0, m2 = 0, m3 = 0;
			for (int b = 0; b < (int)Bucket.Count; b++)
			{
				long t = FrameTicks[b];
				if (t <= 0) continue;
				if (t > m1) { m3 = m2; b3 = b2; m2 = m1; b2 = b1; m1 = t; b1 = b; }
				else if (t > m2) { m3 = m2; b3 = b2; m2 = t; b2 = b; }
				else if (t > m3) { m3 = t; b3 = b; }
			}
			StringBuilder cSb = new StringBuilder();
			if (b1 >= 0) cSb.AppendFormat(CultureInfo.InvariantCulture, "{0} ({1:0.0}ms)", BucketNames[b1].Split(' ')[0], m1 * TicksToMs);
			if (b2 >= 0 && m2 * TicksToMs >= 0.5) cSb.AppendFormat(CultureInfo.InvariantCulture, ", {0} ({1:0.0}ms)", BucketNames[b2].Split(' ')[0], m2 * TicksToMs);
			if (b3 >= 0 && m3 * TicksToMs >= 0.5) cSb.AppendFormat(CultureInfo.InvariantCulture, ", {0} ({1:0.0}ms)", BucketNames[b3].Split(' ')[0], m3 * TicksToMs);
			if (engineGcMs >= 5.0)
			{
				if (cSb.Length > 0) cSb.Append(", ");
				cSb.AppendFormat(CultureInfo.InvariantCulture, "{0} ({1:0.0}ms)", gcRan ? "GC+Render/PhysX" : "Render/PhysX", engineGcMs);
			}

			WorstSpikes.Add(new FrameSpike
			{
				Frame = _renderedFrames,
				FrameMs = frameMs,
				ScriptsMs = scriptsMs,
				EngineGcMs = engineGcMs,
				PhysicsSteps = _framePhysicsSteps,
				GcTriggered = gcRan,
				WaveAdvanced = waveAdvanced,
				TopCauses = cSb.ToString()
			});
			WorstSpikes.Sort((a, b) => b.FrameMs.CompareTo(a.FrameMs));
			if (WorstSpikes.Count > 5) WorstSpikes.RemoveAt(5);
		}

		Array.Clear(FrameTicks, 0, FrameTicks.Length);
		_framePhysicsSteps = 0;
	}

	internal static string EndSessionAndBuildJson()
	{
		Active = false;
		_gc0Delta = Math.Max(0, GC.CollectionCount(0) - _gc0Start);
		_gc1Delta = Math.Max(0, GC.CollectionCount(1) - _gc1Start);
		_gc2Delta = Math.Max(0, GC.CollectionCount(2) - _gc2Start);
		_memEndBytes = GC.GetTotalMemory(false);

		double frames = Math.Max(1, _renderedFrames);
		double physicsPerFrame = _physicsSteps / frames;
		double meanFrameMs = _totalSampleMs / frames;

		// Build sorted bucket entries
		List<(int idx, double msPerFrame, double callsPerFrame, double pctOfFrame, double maxCallMs)> rows =
			new List<(int, double, double, double, double)>((int)Bucket.Count);

		for (int i = 0; i < (int)Bucket.Count; i++)
		{
			double totMs = TotalTicks[i] * TicksToMs;
			double msF = totMs / frames;
			double callsF = Calls[i] / frames;
			double pct = meanFrameMs > 0.001 ? (msF / meanFrameMs) * 100.0 : 0.0;
			double maxMs = MaxTicks[i] * TicksToMs;
			rows.Add((i, msF, callsF, pct, maxMs));
		}
		rows.Sort((a, b) => b.msPerFrame.CompareTo(a.msPerFrame));

		StringBuilder subSb = new StringBuilder("[");
		for (int i = 0; i < rows.Count; i++)
		{
			if (i > 0) subSb.Append(',');
			var r = rows[i];
			subSb.Append(string.Format(
				CultureInfo.InvariantCulture,
				"{{\"name\":{0},\"msPerFrame\":{1:0.000},\"pctOfFrame\":{2:0.0},\"callsPerFrame\":{3:0.0},\"totalCalls\":{4},\"maxCallMs\":{5:0.00}}}",
				JsonStr(BucketNames[r.idx]),
				r.msPerFrame,
				r.pctOfFrame,
				r.callsPerFrame,
				Calls[r.idx],
				r.maxCallMs));
		}
		subSb.Append(']');

		// Top TowerTypes (combined DetectEnemies + Fire CPU time)
		List<(string name, double fireMsF, double targetMsF, double totalMsF, double fireCallsF, double targetCallsF)> towerRows =
			new List<(string, double, double, double, double, double)>(TowerTypeCount);
		for (int t = 0; t < TowerTypeCount; t++)
		{
			double fMs = (TowerFireTicks[t] * TicksToMs) / frames;
			double tMs = (TowerTargetTicks[t] * TicksToMs) / frames;
			if (fMs + tMs <= 0.0001 && TowerFireCalls[t] == 0 && TowerTargetCalls[t] == 0)
			{
				continue;
			}
			string label = ((TowerType)t).ToString();
			towerRows.Add((label, fMs, tMs, fMs + tMs, TowerFireCalls[t] / frames, TowerTargetCalls[t] / frames));
		}
		towerRows.Sort((a, b) => b.totalMsF.CompareTo(a.totalMsF));

		StringBuilder twSb = new StringBuilder("[");
		int twCap = Math.Min(towerRows.Count, 12);
		for (int i = 0; i < twCap; i++)
		{
			if (i > 0) twSb.Append(',');
			var tr = towerRows[i];
			twSb.Append(string.Format(
				CultureInfo.InvariantCulture,
				"{{\"towerType\":{0},\"totalMsPerFrame\":{1:0.000},\"fireMsPerFrame\":{2:0.000},\"detectMsPerFrame\":{3:0.000},\"fireCallsPerFrame\":{4:0.0},\"detectCallsPerFrame\":{5:0.0}}}",
				JsonStr(tr.name),
				tr.totalMsF,
				tr.fireMsF,
				tr.targetMsF,
				tr.fireCallsF,
				tr.targetCallsF));
		}
		twSb.Append(']');

		// Live Entity Census
		int projectiles = UnityEngine.Object.FindObjectsOfType<Projectile>().Length;
		int cannonballs = UnityEngine.Object.FindObjectsOfType<Cannonball>().Length;
		int mortarShells = UnityEngine.Object.FindObjectsOfType<MorterShell>().Length;
		int sawblades = UnityEngine.Object.FindObjectsOfType<Sawblade>().Length;
		int landmines = UnityEngine.Object.FindObjectsOfType<Landmine>().Length;
		int explosions = UnityEngine.Object.FindObjectsOfType<Explosion>().Length;
		int biPlanes = UnityEngine.Object.FindObjectsOfType<BiPlane>().Length;
		int damageNumbers = UnityEngine.Object.FindObjectsOfType<DamageNumber>().Length;

		ParticleSystem[] psAll = UnityEngine.Object.FindObjectsOfType<ParticleSystem>();
		int totalParticles = 0;
		foreach (ParticleSystem ps in psAll)
		{
			if (ps != null) totalParticles += ps.particleCount;
		}

		AudioSource[] audioAll = UnityEngine.Object.FindObjectsOfType<AudioSource>();
		int playingAudio = 0;
		foreach (AudioSource a in audioAll)
		{
			if (a != null && a.isPlaying) playingAudio++;
		}

		double topLevelScriptsMs =
			(TotalTicks[(int)Bucket.TowerDetectEnemies] +
			 TotalTicks[(int)Bucket.TowerUpdateAndXP] +
			 TotalTicks[(int)Bucket.TowerFireAll] +
			 TotalTicks[(int)Bucket.ProjectilesAndAoE] +
			 TotalTicks[(int)Bucket.EnemyUpdateAndDOT] +
			 TotalTicks[(int)Bucket.PathfinderMove] +
			 TotalTicks[(int)Bucket.WalkAnimator] +
			 TotalTicks[(int)Bucket.DropperPathRaycast]) * TicksToMs / frames;
		double enginePhysXRenderMs = Math.Max(0.0, meanFrameMs - topLevelScriptsMs);
		double memDeltaMb = (_memEndBytes - _memStartBytes) / (1024.0 * 1024.0);

		StringBuilder spSb = new StringBuilder("[");
		for (int i = 0; i < WorstSpikes.Count; i++)
		{
			if (i > 0) spSb.Append(',');
			FrameSpike fs = WorstSpikes[i];
			spSb.Append(string.Format(
				CultureInfo.InvariantCulture,
				"{{\"frame\":{0},\"frameMs\":{1:0.00},\"scriptsMs\":{2:0.00},\"engineGcMs\":{3:0.00},\"physicsSteps\":{4},\"gc\":{5},\"waveAdvanced\":{6},\"culprits\":{7}}}",
				fs.Frame,
				fs.FrameMs,
				fs.ScriptsMs,
				fs.EngineGcMs,
				fs.PhysicsSteps,
				fs.GcTriggered ? "true" : "false",
				fs.WaveAdvanced ? "true" : "false",
				JsonStr(fs.TopCauses)));
		}
		spSb.Append(']');

		return string.Format(
			CultureInfo.InvariantCulture,
			"{{\"renderedFrames\":{0},\"physicsSteps\":{1},\"physicsStepsPerFrame\":{2:0.00},\"topLevelScriptsMsPerFrame\":{3:0.00},\"engineRenderPhysXMsPerFrame\":{4:0.00},\"gc0\":{5},\"gc1\":{6},\"gc2\":{7},\"managedMemDeltaMB\":{8:0.0},\"census\":{{\"projectiles\":{9},\"cannonballs\":{10},\"mortarShells\":{11},\"sawblades\":{12},\"landmines\":{13},\"explosions\":{14},\"biPlanes\":{15},\"activeDamageNumbers\":{16},\"particleSystems\":{17},\"liveParticles\":{18},\"playingAudioSources\":{19}}},\"stalls\":{{\"over16ms\":{20},\"over33ms\":{21},\"over50ms\":{22},\"over100ms\":{23},\"worstSpikes\":{24}}},\"subsystems\":{25},\"topTowerTypes\":{26}}}",
			_renderedFrames,
			_physicsSteps,
			physicsPerFrame,
			topLevelScriptsMs,
			enginePhysXRenderMs,
			_gc0Delta,
			_gc1Delta,
			_gc2Delta,
			memDeltaMb,
			projectiles,
			cannonballs,
			mortarShells,
			sawblades,
			landmines,
			explosions,
			biPlanes,
			damageNumbers,
			psAll.Length,
			totalParticles,
			playingAudio,
			_over16Ms,
			_over33Ms,
			_over50Ms,
			_over100Ms,
			spSb.ToString(),
			subSb.ToString(),
			twSb.ToString());
	}

	private static void BuildingManager_FixedUpdate_Postfix()
	{
		if (Active)
		{
			_physicsSteps++;
			_framePhysicsSteps++;
		}
	}

	private static void BeginBucket(int b, out long stamp)
	{
		if (!Active) { stamp = 0; return; }
		Depth[b]++;
		stamp = Stopwatch.GetTimestamp();
	}

	private static void EndBucket(int b, long stamp)
	{
		if (!Active || stamp == 0) return;
		Calls[b]++;
		int rem = Math.Max(0, Depth[b] - 1);
		Depth[b] = rem;
		if (rem == 0)
		{
			long elapsed = Math.Max(0L, Stopwatch.GetTimestamp() - stamp);
			TotalTicks[b] += elapsed;
			FrameTicks[b] += elapsed;
			if (elapsed > MaxTicks[b]) MaxTicks[b] = elapsed;
		}
	}

	private static void DetectEnemies_Prefix(out long __state) => BeginBucket((int)Bucket.TowerDetectEnemies, out __state);

	private static void DetectEnemies_Postfix(Tower __instance, long __state)
	{
		if (!Active || __state == 0) return;
		long elapsed = Math.Max(0L, Stopwatch.GetTimestamp() - __state);
		EndBucket((int)Bucket.TowerDetectEnemies, __state);
		if (__instance != null)
		{
			int ti = (int)__instance.towerType;
			if (ti >= 0 && ti < TowerTypeCount)
			{
				TowerTargetCalls[ti]++;
				TowerTargetTicks[ti] += elapsed;
			}
		}
	}

	private static void TowerFire_Prefix(out long __state)
	{
		if (!Active) { __state = 0; return; }
		_towerFireDepth++;
		BeginBucket((int)Bucket.TowerFireAll, out __state);
	}

	private static void TowerFire_Postfix(Tower __instance, long __state)
	{
		if (!Active || __state == 0) return;
		long elapsed = Math.Max(0L, Stopwatch.GetTimestamp() - __state);
		EndBucket((int)Bucket.TowerFireAll, __state);
		int rem = Math.Max(0, _towerFireDepth - 1);
		_towerFireDepth = rem;
		if (rem == 0 && __instance != null)
		{
			int ti = (int)__instance.towerType;
			if (ti >= 0 && ti < TowerTypeCount)
			{
				TowerFireCalls[ti]++;
				TowerFireTicks[ti] += elapsed;
			}
		}
	}

	// Explicit per-bucket wrappers (0=Detect, 1=TowerUpdate, 2=TowerFire, 3..11)
	private static void Pre_1(out long __state) => BeginBucket(1, out __state);
	private static void Post_1(long __state) => EndBucket(1, __state);

	private static void Pre_3(out long __state) => BeginBucket(3, out __state);
	private static void Post_3(long __state) => EndBucket(3, __state);

	private static void Pre_4(out long __state) => BeginBucket(4, out __state);
	private static void Post_4(long __state) => EndBucket(4, __state);

	private static void Pre_5(out long __state) => BeginBucket(5, out __state);
	private static void Post_5(long __state) => EndBucket(5, __state);

	private static void Pre_6(out long __state) => BeginBucket(6, out __state);
	private static void Post_6(long __state) => EndBucket(6, __state);

	private static void Pre_7(out long __state) => BeginBucket(7, out __state);
	private static void Post_7(long __state) => EndBucket(7, __state);

	private static void Pre_8(out long __state) => BeginBucket(8, out __state);
	private static void Post_8(long __state) => EndBucket(8, __state);

	private static void Pre_9(out long __state) => BeginBucket(9, out __state);
	private static void Post_9(long __state) => EndBucket(9, __state);

	private static void Pre_10(out long __state) => BeginBucket(10, out __state);
	private static void Post_10(long __state) => EndBucket(10, __state);

	private static void Pre_11(out long __state) => BeginBucket(11, out __state);
	private static void Post_11(long __state) => EndBucket(11, __state);

	private static void Pre_12(out long __state) => BeginBucket(12, out __state);
	private static void Post_12(long __state) => EndBucket(12, __state);

	private static string JsonStr(string s)
	{
		if (s == null) return "null";
		return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
	}
}
