using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RogueTowerModMenu;

internal static class TestHarnessServer
{
	private sealed class PendingCommand
	{
		public string Method;
		public string Path;
		public Dictionary<string, string> Params;
		public ManualResetEventSlim Done;
		public int StatusCode = 200;
		public string ResponseJson = "{}";
	}

	private static readonly ConcurrentQueue<PendingCommand> CommandQueue = new ConcurrentQueue<PendingCommand>();
	private static readonly List<string> RecentErrors = new List<string>(32);
	private static readonly object ErrorLock = new object();

	private static readonly Dictionary<string, Func<Dictionary<string, string>, string>> ExternalCommands =
		new Dictionary<string, Func<Dictionary<string, string>, string>>(StringComparer.OrdinalIgnoreCase);
	private static Func<string> _benchmarkStatusProvider;

	private static MonoBehaviour _host;
	private static TcpListener _listener;
	private static Thread _thread;
	private static volatile bool _running;

	private static float _fpsWindowTimer;
	private static int _fpsWindowFrames;
	private static float _currentFps;
	private static float _minFpsInWindow = 999f;
	private static float _avgFps;
	private static float _lastFrameMs;

	private static readonly AccessTools.FieldRef<SpawnManager, HashSet<Waypoint>> SpawnPointsRef =
		AccessTools.FieldRefAccess<SpawnManager, HashSet<Waypoint>>("spawnPoints");

	internal static float CurrentFps => _currentFps;
	internal static float AvgFps => _avgFps;
	internal static float LastFrameMs => _lastFrameMs;

	internal static void RegisterExternalCommand(string op, Func<Dictionary<string, string>, string> handler)
	{
		if (string.IsNullOrWhiteSpace(op) || handler == null) return;
		ExternalCommands[op.Trim().ToLowerInvariant()] = handler;
	}

	internal static void RegisterBenchmarkStatusProvider(Func<string> provider)
	{
		_benchmarkStatusProvider = provider;
	}

	internal static void ClearExternalRegistrations()
	{
		ExternalCommands.Clear();
		_benchmarkStatusProvider = null;
	}

	internal static int GetActivePortalCount()
	{
		if (SpawnManager.instance == null) return 0;
		try
		{
			HashSet<Waypoint> pts = SpawnPointsRef(SpawnManager.instance);
			if (pts != null && pts.Count > 0) return pts.Count;
		}
		catch
		{
		}
		return SpawnManager.instance.tileSpawnUis != null ? SpawnManager.instance.tileSpawnUis.Count : 0;
	}

	internal static string GetOutputDir(string overrideDir = null)
	{
		if (!string.IsNullOrWhiteSpace(overrideDir))
		{
			return overrideDir.Trim();
		}
		string cfg = Plugin.TestOutputDir != null ? Plugin.TestOutputDir.Value : string.Empty;
		if (!string.IsNullOrWhiteSpace(cfg))
		{
			return cfg.Trim();
		}
		return Path.Combine(BepInEx.Paths.BepInExRootPath, "TestOutput");
	}

	internal static void Initialize(MonoBehaviour host)
	{
		_host = host;
		Application.logMessageReceived += OnLogMessage;

		if (!Plugin.EnableTestServer.Value)
		{
			return;
		}

		if (Plugin.RunInBackgroundForTests.Value)
		{
			Application.runInBackground = true;
		}

		int port = Plugin.TestServerPort.Value;
		if (port == 8080 || port < 1024 || port > 65535)
		{
			Plugin.Log.LogWarning($"TestHarnessServer refused unsafe port {port}; falling back to 18437.");
			port = 18437;
		}

		try
		{
			_listener = new TcpListener(IPAddress.Loopback, port);
			_listener.Start();
			_running = true;
			_thread = new Thread(ListenLoop)
			{
				IsBackground = true,
				Name = "RogueTower_TestHarness"
			};
			_thread.Start();
			Plugin.Log.LogInfo($"TestHarnessServer listening on http://127.0.0.1:{port}/");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning($"TestHarnessServer could not bind 127.0.0.1:{port} ({ex.Message}).");
			_running = false;
		}
	}

	internal static void Shutdown()
	{
		_running = false;
		Application.logMessageReceived -= OnLogMessage;
		try
		{
			_listener?.Stop();
		}
		catch
		{
		}
		_listener = null;
	}

	private static void OnLogMessage(string condition, string stackTrace, LogType type)
	{
		if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
		{
			lock (ErrorLock)
			{
				if (RecentErrors.Count >= 25)
				{
					RecentErrors.RemoveAt(0);
				}
				RecentErrors.Add($"[{type}] {condition}");
			}
		}
	}

	internal static void Tick()
	{
		float dt = Time.unscaledDeltaTime;
		if (dt > 0.0001f)
		{
			float instFps = 1f / dt;
			_currentFps = instFps;
			_lastFrameMs = dt * 1000f;
			if (instFps < _minFpsInWindow) _minFpsInWindow = instFps;
			_fpsWindowFrames++;
			_fpsWindowTimer += dt;
			if (_fpsWindowTimer >= 0.5f)
			{
				_avgFps = _fpsWindowFrames / _fpsWindowTimer;
				_fpsWindowFrames = 0;
				_fpsWindowTimer = 0f;
				_minFpsInWindow = instFps;
			}
		}

		int processed = 0;
		while (processed < 8 && CommandQueue.TryDequeue(out PendingCommand cmd))
		{
			processed++;
			try
			{
				ExecuteOnMainThread(cmd);
			}
			catch (Exception ex)
			{
				cmd.StatusCode = 500;
				cmd.ResponseJson = $"{{\"ok\":false,\"error\":{JsonStr(ex.Message)}}}";
				cmd.Done.Set();
			}
		}
	}

	private static void ListenLoop()
	{
		while (_running && _listener != null)
		{
			try
			{
				TcpClient client = _listener.AcceptTcpClient();
				ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
			}
			catch (SocketException)
			{
				if (!_running) break;
			}
			catch
			{
				if (!_running) break;
			}
		}
	}

	private static void HandleClient(TcpClient client)
	{
		using (client)
		{
			client.ReceiveTimeout = 3000;
			client.SendTimeout = 3000;
			try
			{
				using NetworkStream ns = client.GetStream();
				StreamReader reader = new StreamReader(ns, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

				string requestLine = reader.ReadLine();
				if (string.IsNullOrEmpty(requestLine)) return;
				string[] reqParts = requestLine.Split(' ');
				if (reqParts.Length < 2)
				{
					WriteHttp(ns, 400, "{\"ok\":false,\"error\":\"bad request\"}");
					return;
				}

				string method = reqParts[0].ToUpperInvariant();
				string rawTarget = reqParts[1];
				int contentLength = 0;
				string line;
				while (!string.IsNullOrEmpty(line = reader.ReadLine()))
				{
					if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
					{
						int.TryParse(line.Substring(15).Trim(), out contentLength);
					}
				}

				string body = string.Empty;
				if (contentLength > 0 && contentLength < 65536)
				{
					char[] buf = new char[contentLength];
					int readTotal = 0;
					while (readTotal < contentLength)
					{
						int n = reader.Read(buf, readTotal, contentLength - readTotal);
						if (n <= 0) break;
						readTotal += n;
					}
					body = new string(buf, 0, readTotal);
				}

				string path = rawTarget;
				Dictionary<string, string> queryParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				int qIdx = rawTarget.IndexOf('?');
				if (qIdx >= 0)
				{
					path = rawTarget.Substring(0, qIdx);
					ParseFormParams(rawTarget.Substring(qIdx + 1), queryParams);
				}
				if (!string.IsNullOrEmpty(body))
				{
					ParseFormParams(body, queryParams);
				}

				using ManualResetEventSlim done = new ManualResetEventSlim(false);
				PendingCommand cmd = new PendingCommand
				{
					Method = method,
					Path = path,
					Params = queryParams,
					Done = done
				};
				CommandQueue.Enqueue(cmd);

				if (!done.Wait(15000))
				{
					WriteHttp(ns, 504, "{\"ok\":false,\"error\":\"main thread timeout\"}");
					return;
				}

				WriteHttp(ns, cmd.StatusCode, cmd.ResponseJson);
			}
			catch
			{
			}
		}
	}

	private static void ParseFormParams(string input, Dictionary<string, string> dest)
	{
		if (string.IsNullOrWhiteSpace(input)) return;
		string trimmed = input.Trim();
		if (trimmed.StartsWith("{"))
		{
			trimmed = trimmed.Trim('{', '}');
			string[] pairs = trimmed.Split(',');
			foreach (string p in pairs)
			{
				int colon = p.IndexOf(':');
				if (colon <= 0) continue;
				string k = p.Substring(0, colon).Trim().Trim('"', '\'');
				string v = p.Substring(colon + 1).Trim().Trim('"', '\'');
				dest[k] = v;
			}
			return;
		}

		string[] kvps = trimmed.Split('&');
		foreach (string kvp in kvps)
		{
			int eq = kvp.IndexOf('=');
			if (eq <= 0) continue;
			string k = Uri.UnescapeDataString(kvp.Substring(0, eq).Replace('+', ' '));
			string v = Uri.UnescapeDataString(kvp.Substring(eq + 1).Replace('+', ' '));
			dest[k] = v;
		}
	}

	private static void WriteHttp(NetworkStream ns, int statusCode, string json)
	{
		byte[] bodyBytes = Encoding.UTF8.GetBytes(json ?? "{}");
		string statusText = statusCode switch
		{
			200 => "OK",
			400 => "Bad Request",
			404 => "Not Found",
			504 => "Gateway Timeout",
			_ => "Error"
		};
		string headers =
			$"HTTP/1.1 {statusCode} {statusText}\r\n" +
			"Content-Type: application/json; charset=utf-8\r\n" +
			$"Content-Length: {bodyBytes.Length}\r\n" +
			"Connection: close\r\n\r\n";
		byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
		ns.Write(headerBytes, 0, headerBytes.Length);
		ns.Write(bodyBytes, 0, bodyBytes.Length);
		ns.Flush();
	}

	private static void ExecuteOnMainThread(PendingCommand cmd)
	{
		if (cmd.Path.Equals("/status", StringComparison.OrdinalIgnoreCase))
		{
			cmd.ResponseJson = BuildStatusJson();
			cmd.Done.Set();
			return;
		}

		if (cmd.Path.Equals("/benchmark", StringComparison.OrdinalIgnoreCase))
		{
			cmd.ResponseJson = _benchmarkStatusProvider != null
				? _benchmarkStatusProvider()
				: "{\"ok\":true,\"isBusy\":false,\"phase\":\"idle\",\"note\":\"RogueTowerCheats not installed\"}";
			cmd.Done.Set();
			return;
		}

		if (!cmd.Path.Equals("/cmd", StringComparison.OrdinalIgnoreCase))
		{
			cmd.StatusCode = 404;
			cmd.ResponseJson = "{\"ok\":false,\"error\":\"unknown endpoint\"}";
			cmd.Done.Set();
			return;
		}

		cmd.Params.TryGetValue("op", out string op);
		op = (op ?? string.Empty).Trim().ToLowerInvariant();

		if (op == "screenshot")
		{
			if (_host == null)
			{
				cmd.StatusCode = 500;
				cmd.ResponseJson = "{\"ok\":false,\"error\":\"no coroutine host\"}";
				cmd.Done.Set();
				return;
			}
			cmd.Params.TryGetValue("name", out string name);
			cmd.Params.TryGetValue("outDir", out string outDir);
			_host.StartCoroutine(CaptureScreenshotCoroutine(name, outDir, cmd));
			return;
		}

		switch (op)
		{
			case "open_menu":
				ModMenuUI.SetOpen(true);
				cmd.ResponseJson = "{\"ok\":true,\"menuOpen\":true}";
				break;

			case "close_menu":
				ModMenuUI.SetOpen(false);
				cmd.ResponseJson = "{\"ok\":true,\"menuOpen\":false}";
				break;

			case "select_tab":
				if (cmd.Params.TryGetValue("tab", out string tabStr))
				{
					ModMenuUI.SetOpen(true);
					if (int.TryParse(tabStr, out int tabIdx))
					{
						ModMenuUI.SelectTabIndex(tabIdx);
					}
					else
					{
						ModMenuUI.SelectTabByIdOrTitle(tabStr);
					}
					cmd.ResponseJson = $"{{\"ok\":true,\"tabIndex\":{ModMenuUI.CurrentTabIndex},\"tabId\":{JsonStr(ModMenuUI.CurrentTabId)}}}";
				}
				else
				{
					cmd.StatusCode = 400;
					cmd.ResponseJson = "{\"ok\":false,\"error\":\"missing tab\"}";
				}
				break;

			case "start_run":
			{
				int mode = 1;
				if (cmd.Params.TryGetValue("mode", out string mStr)) int.TryParse(mStr, out mode);
				PlayerPrefs.SetInt("GameMode", Mathf.Clamp(mode, 1, 4));
				if (LevelLoader.instance != null)
				{
					LevelLoader.instance.LoadLevel("GameScene");
				}
				else
				{
					SceneManager.LoadScene("GameScene");
				}
				cmd.ResponseJson = $"{{\"ok\":true,\"mode\":{mode}}}";
				break;
			}

			case "main_menu":
				if (LevelLoader.instance != null)
				{
					LevelLoader.instance.LoadLevel("MainMenu");
				}
				else
				{
					SceneManager.LoadScene("MainMenu");
				}
				cmd.ResponseJson = "{\"ok\":true}";
				break;

			case "set_speed":
			{
				if (cmd.Params.TryGetValue("val", out string sVal) && float.TryParse(sVal, NumberStyles.Float, CultureInfo.InvariantCulture, out float spd))
				{
					ModuleBridge.SetSpeed(spd);
					cmd.ResponseJson = $"{{\"ok\":true,\"speed\":{spd.ToString(CultureInfo.InvariantCulture)}}}";
				}
				else
				{
					cmd.StatusCode = 400;
					cmd.ResponseJson = "{\"ok\":false,\"error\":\"invalid val\"}";
				}
				break;
			}

			case "rotate_camera":
			{
				float deg = 90f;
				if (cmd.Params.TryGetValue("deg", out string dStr)) float.TryParse(dStr, NumberStyles.Float, CultureInfo.InvariantCulture, out deg);
				ModuleBridge.RotateCameraBy(deg);
				cmd.ResponseJson = $"{{\"ok\":true,\"targetYaw\":{ModuleBridge.GetCameraYaw().ToString("0.#", CultureInfo.InvariantCulture)}}}";
				break;
			}

			case "toggle_topdown":
				ModuleBridge.ToggleTopDown();
				cmd.ResponseJson = $"{{\"ok\":true,\"topDown\":{(ModuleBridge.IsTopDownActive() ? "true" : "false")}}}";
				break;

			case "open_hotbar_sort":
			{
				bool open = !cmd.Params.TryGetValue("open", out string oStr) || !string.Equals(oStr, "false", StringComparison.OrdinalIgnoreCase);
				bool ok = ModuleBridge.SetQoLHotbarSortOpen(open);
				cmd.ResponseJson = $"{{\"ok\":{(ok ? "true" : "false")},\"open\":{(open ? "true" : "false")}}}";
				break;
			}

			case "simulate_hotbar_drag":
			{
				cmd.Params.TryGetValue("id", out string id);
				int idx = 0;
				if (cmd.Params.TryGetValue("index", out string iStr)) int.TryParse(iStr, out idx);
				bool all = cmd.Params.TryGetValue("all", out string aStr) && string.Equals(aStr, "true", StringComparison.OrdinalIgnoreCase);
				bool ok = ModuleBridge.MoveQoLHotbarItemToIndex(id, idx, all);
				if (ModMenuUI.IsMenuOpen) ModMenuUI.RefreshOpenTabPublic();
				cmd.ResponseJson = $"{{\"ok\":{(ok ? "true" : "false")}}}";
				break;
			}

			case "save_hotbar_preset":
			{
				int slot = 1;
				if (cmd.Params.TryGetValue("slot", out string sStr)) int.TryParse(sStr, out slot);
				bool ok = ModuleBridge.SaveQoLHotbarPreset(Mathf.Clamp(slot, 1, 3));
				cmd.ResponseJson = $"{{\"ok\":{(ok ? "true" : "false")},\"slot\":{Mathf.Clamp(slot, 1, 3)}}}";
				break;
			}

			case "load_hotbar_preset":
			{
				int slot = 1;
				if (cmd.Params.TryGetValue("slot", out string sStr)) int.TryParse(sStr, out slot);
				bool ok = ModuleBridge.LoadQoLHotbarPreset(Mathf.Clamp(slot, 1, 3));
				if (ModMenuUI.IsMenuOpen) ModMenuUI.RefreshOpenTabPublic();
				cmd.ResponseJson = $"{{\"ok\":{(ok ? "true" : "false")},\"slot\":{Mathf.Clamp(slot, 1, 3)}}}";
				break;
			}

			case "open_map_editor":
			{
				bool ok = ModuleBridge.OpenCustomMapEditor();
				cmd.ResponseJson = $"{{\"ok\":{(ok ? "true" : "false")}}}";
				break;
			}

			case "click_button":
			{
				cmd.Params.TryGetValue("name", out string btnName);
				bool clicked = ClickButtonByName(btnName);
				cmd.ResponseJson = $"{{\"ok\":{(clicked ? "true" : "false")}}}";
				break;
			}

			case "clear_errors":
				lock (ErrorLock) RecentErrors.Clear();
				cmd.ResponseJson = "{\"ok\":true}";
				break;

			case "set_mute":
			{
				string rawMute = null;
				if (!cmd.Params.TryGetValue("enabled", out rawMute))
				{
					cmd.Params.TryGetValue("muted", out rawMute);
				}
				if (rawMute == null)
				{
					cmd.Params.TryGetValue("val", out rawMute);
				}
				bool mute = rawMute == null || !string.Equals(rawMute, "false", StringComparison.OrdinalIgnoreCase);
				Plugin.MuteAudioForTests.Value = mute;
				AudioListener.volume = mute ? 0f : 1f;
				cmd.ResponseJson = $"{{\"ok\":true,\"muted\":{(mute ? "true" : "false")}}}";
				break;
			}

			case "set_lagfix":
			{
				bool enabled = !cmd.Params.TryGetValue("enabled", out string eStr) || !string.Equals(eStr, "false", StringComparison.OrdinalIgnoreCase);
				bool loaded = ApplyLagFixAll(enabled);
				if (ModMenuUI.IsMenuOpen) ModMenuUI.RefreshOpenTabPublic();
				cmd.ResponseJson = $"{{\"ok\":true,\"lagFixLoaded\":{(loaded ? "true" : "false")},\"enabled\":{(enabled ? "true" : "false")}}}";
				break;
			}

			case "set_theme":
			{
				cmd.Params.TryGetValue("mode", out string mStr);
				int modeIdx = string.Equals(mStr, "light", StringComparison.OrdinalIgnoreCase) ? 2
					: string.Equals(mStr, "vanilla", StringComparison.OrdinalIgnoreCase) ? 0
					: 1;
				ModuleBridge.SetThemeMode(modeIdx);
				if (ModMenuUI.IsMenuOpen) ModMenuUI.RefreshOpenTabPublic();
				cmd.ResponseJson = $"{{\"ok\":true,\"themeMode\":{JsonStr(ModuleBridge.GetThemeModeName())}}}";
				break;
			}

			case "set_visuals":
			{
				cmd.Params.TryGetValue("preset", out string vp);
				string key = (vp ?? "lagfix_balanced").Trim().ToLowerInvariant();
				bool lagOn = key != "vanilla_full";
				bool dmgNums = key != "minimal_fx";
				bool extraFx = key != "minimal_fx";
				ApplyLagFixAll(lagOn);
				if (OptionsMenu.instance != null)
				{
					OptionsMenu.instance.showDamageNumbers = dmgNums;
					OptionsMenu.instance.extraProjectileEffects = extraFx;
				}
				if (ModuleBridge.HasCleanQoL)
				{
					bool qolCircles = key != "minimal_fx";
					ModuleBridge.SetConfig(ModuleBridge.Keys.CleanQoL, "1. Range Circles", "EnableHoverRange", qolCircles);
					ModuleBridge.SetConfig(ModuleBridge.Keys.CleanQoL, "2. Targeting Priorities", "EnablePriorityColors", qolCircles);
				}
				else if (ModuleBridge.IsLoaded(ModuleBridge.Keys.QoL))
				{
					bool qolCircles = key != "minimal_fx";
					ModuleBridge.SetConfig(ModuleBridge.Keys.QoL, "General", "EnableHoverRangeCircle", qolCircles);
					ModuleBridge.SetConfig(ModuleBridge.Keys.QoL, "General", "EnablePriorityColors", qolCircles);
				}
				if (ModMenuUI.IsMenuOpen) ModMenuUI.RefreshOpenTabPublic();
				cmd.ResponseJson = $"{{\"ok\":true,\"visualPreset\":{JsonStr(key)},\"lagFix\":{(lagOn ? "true" : "false")},\"showDamageNumbers\":{(dmgNums ? "true" : "false")},\"extraProjectileFX\":{(extraFx ? "true" : "false")}}}";
				break;
			}

			default:
				if (ExternalCommands.TryGetValue(op, out Func<Dictionary<string, string>, string> extHandler) && extHandler != null)
				{
					cmd.ResponseJson = extHandler(cmd.Params) ?? "{\"ok\":true}";
				}
				else
				{
					cmd.StatusCode = 400;
					cmd.ResponseJson = $"{{\"ok\":false,\"error\":{JsonStr("unknown op: " + op)}}}";
				}
				break;
		}

		cmd.Done.Set();
	}

	private static bool ApplyLagFixAll(bool enabled)
	{
		bool loaded = ModuleBridge.IsLoaded(ModuleBridge.Keys.LagFix);
		if (loaded)
		{
			ModuleBridge.SetConfig(ModuleBridge.Keys.LagFix, "1. CPU & Targeting", "OptimizeFastForwardPhysics", enabled);
			ModuleBridge.SetConfig(ModuleBridge.Keys.LagFix, "1. CPU & Targeting", "OptimizeTargetingAndEnemyLookups", enabled);
			ModuleBridge.SetConfig(ModuleBridge.Keys.LagFix, "2. UI & HealthBars", "OptimizeEnemyHealthBars", enabled);
			ModuleBridge.SetConfig(ModuleBridge.Keys.LagFix, "3. Damage Numbers", "ThrottleDamageNumbers", enabled);
			ModuleBridge.SetConfig(ModuleBridge.Keys.LagFix, "4. Visual FX", "OptimizeProjectileAndExplosionFX", enabled);
			ModuleBridge.SetConfig(ModuleBridge.Keys.LagFix, "4. Visual FX", "FixTeslaAndVampireTrailCoroutines", enabled);
			ModuleBridge.SetConfig(ModuleBridge.Keys.LagFix, "5. Audio", "DeduplicateCombatAudio", enabled);
		}
		return loaded;
	}

	private static bool ClickButtonByName(string name)
	{
		if (string.IsNullOrEmpty(name)) return false;
		Button[] all = UnityEngine.Object.FindObjectsOfType<Button>(includeInactive: false);
		foreach (Button b in all)
		{
			if (b != null && b.interactable && string.Equals(b.gameObject.name, name, StringComparison.OrdinalIgnoreCase))
			{
				b.onClick.Invoke();
				return true;
			}
		}
		return false;
	}

	private static IEnumerator CaptureScreenshotCoroutine(string rawName, string outDirOverride, PendingCommand cmd)
	{
		yield return new WaitForEndOfFrame();
		try
		{
			string safe = string.IsNullOrWhiteSpace(rawName)
				? ("rt_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"))
				: SanitizeFileName(rawName);
			if (!safe.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
			{
				safe += ".png";
			}

			string shotDir = Path.Combine(GetOutputDir(outDirOverride), "screenshots");
			Directory.CreateDirectory(shotDir);
			string fullPath = Path.Combine(shotDir, safe);

			Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
			byte[] png = ImageConversion.EncodeToPNG(tex);
			int w = tex.width;
			int h = tex.height;
			UnityEngine.Object.Destroy(tex);

			File.WriteAllBytes(fullPath, png);
			cmd.ResponseJson = $"{{\"ok\":true,\"path\":{JsonStr(fullPath)},\"width\":{w},\"height\":{h}}}";
		}
		catch (Exception ex)
		{
			cmd.StatusCode = 500;
			cmd.ResponseJson = $"{{\"ok\":false,\"error\":{JsonStr(ex.Message)}}}";
		}
		finally
		{
			cmd.Done.Set();
		}
	}

	private static string SanitizeFileName(string name)
	{
		foreach (char c in Path.GetInvalidFileNameChars())
		{
			name = name.Replace(c, '_');
		}
		return name;
	}

	private static readonly AccessTools.FieldRef<ResourceManager, int> GoldRef =
		AccessTools.FieldRefAccess<ResourceManager, int>("gold");

	private static string BuildStatusJson()
	{
		string scene = SceneManager.GetActiveScene().name;
		int wave = SpawnManager.instance != null ? SpawnManager.instance.level : 0;
		int gold = ResourceManager.instance != null ? GoldRef(ResourceManager.instance) : 0;
		int hp = GameManager.instance != null ? GameManager.instance.health / 10 : 0;
		int maxHp = GameManager.instance != null ? GameManager.instance.maxHealth / 10 : 0;
		int enemies = SpawnManager.instance != null ? SpawnManager.instance.currentEnemies.Count : 0;
		int placedTowers = UnityEngine.Object.FindObjectsOfType<Tower>().Length;
		int activePortals = GetActivePortalCount();

		List<string> hotbarTowers = Plugin.IsInGameRun && ModuleBridge.HasCleanQoL ? ModuleBridge.GetQoLActiveHotbarIds(true) : new List<string>();
		List<string> hotbarBlds = Plugin.IsInGameRun && ModuleBridge.HasCleanQoL ? ModuleBridge.GetQoLActiveHotbarIds(false) : new List<string>();

		string errorsJson;
		lock (ErrorLock)
		{
			StringBuilder eSb = new StringBuilder("[");
			for (int i = 0; i < RecentErrors.Count; i++)
			{
				if (i > 0) eSb.Append(',');
				eSb.Append(JsonStr(RecentErrors[i]));
			}
			eSb.Append(']');
			errorsJson = eSb.ToString();
		}

		return string.Format(
			CultureInfo.InvariantCulture,
			"{{\"ok\":true,\"scene\":{0},\"fps\":{1:0.0},\"avgFps\":{2:0.0},\"minFps\":{3:0.0},\"frameTimeMs\":{4:0.00},\"timeScale\":{5:0.##},\"wave\":{6},\"gold\":{7},\"hp\":{8},\"maxHp\":{9},\"enemies\":{10},\"placedTowers\":{11},\"activePortals\":{12},\"cheatsModLoaded\":{13},\"themeMode\":{14},\"menuOpen\":{15},\"activeTab\":{16},\"activeTabId\":{17},\"cameraYaw\":{18:0.#},\"topDown\":{19},\"hotbarTowers\":{20},\"hotbarBuildings\":{21},\"errors\":{22}}}",
			JsonStr(scene),
			_currentFps,
			_avgFps,
			_minFpsInWindow > 900f ? _currentFps : _minFpsInWindow,
			_lastFrameMs,
			Time.timeScale,
			wave,
			gold,
			hp,
			maxHp,
			enemies,
			placedTowers,
			activePortals,
			ModuleBridge.HasCheatsMod ? "true" : "false",
			JsonStr(ModuleBridge.GetThemeModeName()),
			ModMenuUI.IsMenuOpen ? "true" : "false",
			ModMenuUI.CurrentTabIndex,
			JsonStr(ModMenuUI.CurrentTabId),
			ModuleBridge.GetCameraYaw(),
			ModuleBridge.IsTopDownActive() ? "true" : "false",
			JsonStringArray(hotbarTowers),
			JsonStringArray(hotbarBlds),
			errorsJson);
	}

	private static string JsonStringArray(List<string> items)
	{
		StringBuilder sb = new StringBuilder("[");
		for (int i = 0; i < items.Count; i++)
		{
			if (i > 0) sb.Append(',');
			sb.Append(JsonStr(items[i]));
		}
		sb.Append(']');
		return sb.ToString();
	}

	private static string JsonStr(string s)
	{
		if (s == null) return "null";
		return "\"" + s
			.Replace("\\", "\\\\")
			.Replace("\"", "\\\"")
			.Replace("\r", "\\r")
			.Replace("\n", "\\n") + "\"";
	}
}
