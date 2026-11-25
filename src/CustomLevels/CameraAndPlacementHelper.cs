using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RogueTowerCustomLevels;

internal static class CameraAndPlacementPatches
{
	private static readonly AccessTools.FieldRef<BuildingManager, LayerMask> BuildableMaskRef =
		AccessTools.FieldRefAccess<BuildingManager, LayerMask>("buildableMask");
	private static readonly MethodInfo DisplayGhostMethod =
		typeof(BuildingManager).GetMethod("DisplayGhost", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly AccessTools.FieldRef<UIManager, LayerMask> ClickableMaskRef =
		AccessTools.FieldRefAccess<UIManager, LayerMask>("clickableMask");
	private static readonly AccessTools.FieldRef<UIManager, GameObject> CurrentUIRef =
		AccessTools.FieldRefAccess<UIManager, GameObject>("currentUI");

	[HarmonyPatch(typeof(CameraController), "Start")]
	[HarmonyPostfix]
	private static void CameraController_Start_Postfix(CameraController __instance)
	{
		CameraAndPlacementHelper.OnCameraStart(__instance);
	}

	[HarmonyPatch(typeof(CameraController), "UpdateZoom")]
	[HarmonyPostfix]
	private static void CameraController_UpdateZoom_Postfix(CameraController __instance)
	{
		CameraAndPlacementHelper.ApplyCameraRig(__instance);
	}

	[HarmonyPatch(typeof(BuildingManager), "SamplePoint")]
	[HarmonyPostfix]
	private static void BuildingManager_SamplePoint_Postfix(BuildingManager __instance, ref bool __result)
	{
		if (!Plugin.EnableXRayPlacement.Value || __instance == null || Camera.main == null)
		{
			return;
		}
		bool altHeld = Input.GetKey(KeyCode.LeftAlt);
		if (__result && !altHeld)
		{
			return;
		}

		LayerMask mask = BuildableMaskRef(__instance);
		int grassLayer = LayerMask.NameToLayer("Grass");
		RaycastHit[] hits = Physics.RaycastAll(Camera.main.ScreenPointToRay(Input.mousePosition), 2000f, mask, QueryTriggerInteraction.Ignore);
		if (hits == null || hits.Length == 0)
		{
			return;
		}
		Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

		List<Vector3> validPositions = new List<Vector3>(3);
		for (int i = 0; i < hits.Length; i++)
		{
			RaycastHit hit = hits[i];
			if (hit.collider == null || hit.collider.gameObject.layer != grassLayer)
			{
				continue;
			}
			// Ignore vertical cliff faces; only place on horizontal grass tile tops
			if (hit.normal.y < 0.75f)
			{
				continue;
			}

			Vector3 gridPos = new Vector3(
				Mathf.Round(hit.point.x),
				Mathf.Round(3f * hit.point.y) / 3f,
				Mathf.Round(hit.point.z));

			float dx = hit.point.x - gridPos.x;
			float dz = hit.point.z - gridPos.z;
			if (dx * dx + dz * dz >= 0.25f)
			{
				continue;
			}

			if (IsTileOccupied(gridPos))
			{
				continue;
			}

			validPositions.Add(gridPos);
		}

		if (validPositions.Count == 0)
		{
			return;
		}

		// When holding LeftAlt over overlapping elevated + lower tiles, pick the deeper tile behind the foreground cliff
		Vector3 chosen = (altHeld && validPositions.Count > 1) ? validPositions[1] : validPositions[0];
		string text = chosen.y > 0.34f ? ("+" + (Mathf.RoundToInt(chosen.y * 3f) - 1)) : string.Empty;
		DisplayGhostMethod?.Invoke(__instance, new object[] { chosen, text });
		__result = true;
	}

	private static bool IsTileOccupied(Vector3 gridPos)
	{
		Collider[] overlap = Physics.OverlapSphere(gridPos + Vector3.up * 0.35f, 0.28f, ~0, QueryTriggerInteraction.Collide);
		for (int i = 0; i < overlap.Length; i++)
		{
			Collider col = overlap[i];
			if (col == null) continue;
			if (col.GetComponentInParent<IBuildable>() != null)
			{
				return true;
			}
		}
		return false;
	}

	[HarmonyPatch(typeof(UIManager), "Update")]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.Low)]
	private static bool UIManager_Update_AltSelect_Prefix(UIManager __instance)
	{
		if (!Plugin.EnableAltCutaway.Value || __instance == null || Camera.main == null || BuildingManager.instance == null)
		{
			return true;
		}
		if (BuildingManager.instance.buildMode || ModMenuUI.IsPointerOverMenu || QuickHudManager.IsPointerOverQuickButton())
		{
			return true;
		}
		if (!Input.GetKey(KeyCode.LeftAlt) || !Input.GetMouseButtonDown(0))
		{
			return true;
		}

		LayerMask mask = ClickableMaskRef(__instance);
		RaycastHit[] hits = Physics.RaycastAll(Camera.main.ScreenPointToRay(Input.mousePosition), 2000f, mask, QueryTriggerInteraction.Collide);
		if (hits == null || hits.Length == 0)
		{
			return true;
		}
		Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

		List<IBuildable> buildables = new List<IBuildable>(4);
		for (int i = 0; i < hits.Length; i++)
		{
			IBuildable b = hits[i].collider?.GetComponentInParent<IBuildable>();
			if (b != null && !buildables.Contains(b))
			{
				buildables.Add(b);
			}
		}
		if (buildables.Count > 0)
		{
			IBuildable pick = buildables.Count > 1 ? buildables[buildables.Count - 1] : buildables[0];
			pick.SpawnUI();
			return false;
		}
		return true;
	}
}

internal static class CameraAndPlacementHelper
{
	private static readonly AccessTools.FieldRef<CameraController, GameObject> CameraHolderRef =
		AccessTools.FieldRefAccess<CameraController, GameObject>("cameraHolder");
	private static readonly AccessTools.FieldRef<BuildingManager, GameObject> CurrentGhostRef =
		AccessTools.FieldRefAccess<BuildingManager, GameObject>("currentGhost");

	private static float _baseYaw = 45f;
	private static float _targetYawOffset;
	private static float _currentYawOffset;
	private static bool _topDownManual;
	private static float _topDownBlend;
	private static Quaternion _defaultHolderLocalRot = Quaternion.identity;
	private static bool _capturedHolderRot;

	// Alt-cutaway tower scaling cache
	private static bool _cutawayActive;
	private static readonly Dictionary<Transform, Vector3> SavedChildScales = new Dictionary<Transform, Vector3>();

	internal static float CurrentYawOffset => _currentYawOffset;
	internal static bool IsTopDown => _topDownManual || (Plugin.AutoTopDownInBuildMode.Value && BuildingManager.instance != null && BuildingManager.instance.buildMode);

	internal static void OnSceneChanged()
	{
		RestoreCutawayScales();
		_targetYawOffset = 0f;
		_currentYawOffset = 0f;
		_topDownManual = false;
		_topDownBlend = 0f;
		_capturedHolderRot = false;
	}

	internal static void OnCameraStart(CameraController cam)
	{
		if (cam == null || cam.firstPersonMode)
		{
			return;
		}
		_baseYaw = cam.transform.eulerAngles.y;
		_targetYawOffset = 0f;
		_currentYawOffset = 0f;
		_topDownManual = false;
		_topDownBlend = 0f;

		GameObject holder = CameraHolderRef(cam);
		if (holder != null)
		{
			_defaultHolderLocalRot = holder.transform.localRotation;
			_capturedHolderRot = true;
		}
	}

	internal static void RotateBy(float degrees)
	{
		_targetYawOffset = (_targetYawOffset + degrees) % 360f;
		if (_targetYawOffset < 0f)
		{
			_targetYawOffset += 360f;
		}
	}

	internal static void ToggleTopDown()
	{
		_topDownManual = !_topDownManual;
	}

	internal static void ResetCameraView()
	{
		_targetYawOffset = 0f;
		_topDownManual = false;
	}

	internal static void Tick()
	{
		CameraController cam = CameraController.instance;
		if (cam == null || cam.firstPersonMode || Camera.main == null)
		{
			RestoreCutawayScales();
			return;
		}

		bool typing = ModMenuUI.IsSearchFocused;
		if (!typing && Plugin.EnableCameraRotation.Value)
		{
			if (Input.GetKeyDown(Plugin.RotateLeftKey.Value))
			{
				RotateBy(-90f);
			}
			if (Input.GetKeyDown(Plugin.RotateRightKey.Value))
			{
				RotateBy(90f);
			}
			bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
			if (!ctrl && Input.GetKeyDown(Plugin.TopDownToggleKey.Value))
			{
				ToggleTopDown();
			}
		}

		float dt = Time.unscaledDeltaTime;
		_currentYawOffset = Mathf.LerpAngle(_currentYawOffset, _targetYawOffset, Mathf.Clamp01(dt * 14f));
		bool wantTop = IsTopDown;
		_topDownBlend = Mathf.MoveTowards(_topDownBlend, wantTop ? 1f : 0f, dt * 6f);

		ApplyCameraRig(cam);
		BillboardWorldUIs();
		UpdateCutaway(!typing && Plugin.EnableAltCutaway.Value && Input.GetKey(KeyCode.LeftAlt));
	}

	internal static void ApplyCameraRig(CameraController cam)
	{
		if (cam == null || cam.firstPersonMode || Camera.main == null)
		{
			return;
		}
		cam.transform.rotation = Quaternion.Euler(0f, _baseYaw + _currentYawOffset, 0f);

		if (!_capturedHolderRot)
		{
			return;
		}
		GameObject holder = CameraHolderRef(cam);
		if (holder == null)
		{
			return;
		}

		float zoom = Camera.main.orthographicSize;
		Vector3 isoPos = new Vector3(0f, 5f + 2f * zoom, -2f * zoom - 5f);
		Vector3 topPos = new Vector3(0f, 12f + 2.5f * zoom, -0.01f);
		Quaternion topRot = Quaternion.Euler(90f, 0f, 0f);

		holder.transform.localPosition = Vector3.Lerp(isoPos, topPos, _topDownBlend);
		holder.transform.localRotation = Quaternion.Slerp(_defaultHolderLocalRot, topRot, _topDownBlend);
	}

	private static void BillboardWorldUIs()
	{
		bool rotated = Mathf.Abs(Mathf.DeltaAngle(0f, _currentYawOffset)) > 0.5f || _topDownBlend > 0.01f;
		if (!rotated || Camera.main == null)
		{
			return;
		}

		Quaternion camRot = Camera.main.transform.rotation;
		if (SpawnManager.instance != null && SpawnManager.instance.tileSpawnUis != null)
		{
			foreach (GameObject ui in SpawnManager.instance.tileSpawnUis)
			{
				if (ui != null && ui.activeInHierarchy)
				{
					ui.transform.rotation = camRot;
				}
			}
		}

		if (BuildingManager.instance != null)
		{
			GameObject ghost = CurrentGhostRef(BuildingManager.instance);
			if (ghost != null && ghost.activeInHierarchy)
			{
				ghost.transform.rotation = camRot;
			}
		}
	}

	private static void UpdateCutaway(bool active)
	{
		if (active == _cutawayActive && !active)
		{
			return;
		}

		if (!active)
		{
			RestoreCutawayScales();
			return;
		}

		_cutawayActive = true;
		Tower[] towers = UnityEngine.Object.FindObjectsOfType<Tower>();
		for (int i = 0; i < towers.Length; i++)
		{
			Tower t = towers[i];
			if (t == null) continue;
			Transform root = t.transform;
			for (int c = 0; c < root.childCount; c++)
			{
				Transform child = root.GetChild(c);
				if (child == null) continue;
				if (child.GetComponent<Canvas>() != null || child.GetComponent<LineRenderer>() != null)
				{
					continue;
				}
				if (!SavedChildScales.ContainsKey(child))
				{
					SavedChildScales[child] = child.localScale;
					child.localScale = new Vector3(child.localScale.x, Mathf.Min(child.localScale.y, child.localScale.y * 0.18f), child.localScale.z);
				}
			}
		}
	}

	private static void RestoreCutawayScales()
	{
		if (!_cutawayActive && SavedChildScales.Count == 0)
		{
			return;
		}
		_cutawayActive = false;
		foreach (KeyValuePair<Transform, Vector3> kvp in SavedChildScales)
		{
			if (kvp.Key != null)
			{
				kvp.Key.localScale = kvp.Value;
			}
		}
		SavedChildScales.Clear();
	}
}
