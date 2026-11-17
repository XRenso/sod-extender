using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace SodDevBridge
{
    /// <summary>
    /// DEV ONLY. File RPC between dev/tools/sod.py and the running game.
    /// sod.py writes dev/bridge/request.json (atomically); Tick() runs it on the
    /// Unity main thread and writes dev/bridge/response.json.
    /// </summary>
    [BepInPlugin("dev.sod.bridge", "SodDevBridge", "1.0.0")]
    public class Plugin : BasePlugin
    {
        public static ManualLogSource L;
        public static string BridgeDir;

        public override void Load()
        {
            L = Log;
            // Only sod.py passes this flag. A normal launch by the user stays untouched
            // (no mute, no runInBackground, no file polling).
            if (!Environment.GetCommandLineArgs().Contains("--sod-dev"))
            {
                L.LogInfo("SodDevBridge idle: no --sod-dev flag");
                return;
            }
            BridgeDir = Path.Combine(Paths.GameRootPath, "dev", "bridge");
            Directory.CreateDirectory(BridgeDir);
            ClassInjector.RegisterTypeInIl2Cpp<BridgeBehaviour>();
            var go = new GameObject("SodDevBridge");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<BridgeBehaviour>();
            L.LogInfo("SodDevBridge loaded");
        }
    }

    /// <summary>Helpers available to exec snippets and in-game tests (using static SodDevBridge.Api).</summary>
    public static class Api
    {
        internal static readonly StringBuilder Output = new StringBuilder();

        /// <summary>Print to the exec reply and to the BepInEx log.</summary>
        public static void P(params object[] values)
        {
            var line = string.Join(" ", values.Select(Format));
            Output.AppendLine(line);
            Plugin.L.LogInfo("[exec] " + line);
        }

        /// <summary>Text form of any value; collections print their items (max 200).</summary>
        public static string Format(object v)
        {
            if (v == null) return "null";
            if (v is string s) return s;
            if (v is IEnumerable e)
            {
                var items = new List<string>();
                foreach (var x in e)
                {
                    if (items.Count == 200) { items.Add("..."); break; }
                    items.Add(Format(x));
                }
                return "[" + string.Join(", ", items) + "]";
            }
            return v.ToString();
        }

        /// <summary>How many uploads went through the Private guard.</summary>
        public static int PrivateUploads;

        /// <summary>Every Workshop submit from this session becomes Private, whatever the caller set.
        /// The game's own uploader has no visibility option, so tests must call this first.</summary>
        public static void ForcePrivateWorkshopUploads()
        {
            var target = AccessTools.Method(typeof(Steamworks.SteamUGC), nameof(Steamworks.SteamUGC.SubmitItemUpdate));
            new Harmony("dev.sod.bridge.private").Patch(target, prefix: new HarmonyMethod(typeof(Api), nameof(SubmitPrefix)));
            if (Harmony.GetPatchInfo(target) == null) throw new Exception("Private guard is not applied");
        }

        private static void SubmitPrefix(Steamworks.UGCUpdateHandle_t handle)
        {
            var ok = Steamworks.SteamUGC.SetItemVisibility(handle, Steamworks.ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate);
            if (ok) PrivateUploads++;
            Plugin.L.LogInfo("[workshop] forced Private: " + ok);
        }

        /// <summary>Fail the current test.</summary>
        public static void Check(bool condition, string message = "check failed")
        {
            if (!condition) throw new Exception(message);
        }

        /// <summary>Yield in a test to wait for real seconds (works while the game is paused).</summary>
        public static IEnumerator Wait(float seconds)
        {
            var end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        /// <summary>Yield in a test until the predicate is true. Throws on timeout.</summary>
        public static IEnumerator WaitUntil(Func<bool> predicate, float timeoutSeconds = 30f)
        {
            var end = Time.realtimeSinceStartup + timeoutSeconds;
            while (!predicate())
            {
                if (Time.realtimeSinceStartup > end) throw new TimeoutException("WaitUntil timed out");
                yield return null;
            }
        }
    }

    public class BridgeBehaviour : MonoBehaviour
    {
        public BridgeBehaviour(IntPtr ptr) : base(ptr) { }

        private float _nextPoll;
        private float _nextHeartbeat;
        private bool _muted;

        private class TestRun
        {
            public string Id;
            public List<MethodInfo> Queue;
            public int Index;
            public IEnumerator Current;
            public float CaseStart;
            public float CaseTimeout;
            public List<Dictionary<string, object>> Results = new();
        }
        private TestRun _test;

        private void Update()
        {
            Application.runInBackground = true;
            if (!_muted) _muted = TryMute();

            var now = Time.realtimeSinceStartup;
            if (now >= _nextHeartbeat)
            {
                _nextHeartbeat = now + 1f;
                TryWriteText("heartbeat.txt", Heartbeat());
                // The game applies its own display options when the menu loads.
                // Keep the dev window, so the game does not cover the user's screen.
                if (Screen.fullScreen || Screen.width != 1280 || Screen.height != 720)
                    Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            }
            StepTests();
            if (now < _nextPoll) return;
            _nextPoll = now + 0.05f;
            PollRequest();
        }

        /// <summary>The game must stay silent while Claude drives it. Kept in a separate
        /// method so a missing FMOD type only disables mute, not the bridge.</summary>
        private static bool TryMute()
        {
            try
            {
                FMODUnity.RuntimeManager.MuteAllEvents(true);
                return true;
            }
            catch (Exception)
            {
                return false; // FMOD not ready yet: try again next frame.
            }
        }

        /// <summary>One line for hang triage: if the file stops changing, the main thread is blocked
        /// at this frame and scene. sod.py only reads the file time, so the format is free.</summary>
        private static string Heartbeat()
        {
            var line = $"{DateTime.UtcNow:o} frame={Time.frameCount} t={Time.realtimeSinceStartup:F0}";
            try
            {
                line += " scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                var s = SessionData.Instance;
                line += " startedGame=" + (s != null && s.startedGame);
            }
            catch (Exception ex) { line += " info-error=" + ex.GetType().Name; }
            return line;
        }

        private static void TryWriteText(string name, string text)
        {
            try { File.WriteAllText(Path.Combine(Plugin.BridgeDir, name), text); }
            catch (IOException) { /* sod.py may be reading it; next tick wins. */ }
        }

        private void PollRequest()
        {
            var reqPath = Path.Combine(Plugin.BridgeDir, "request.json");
            if (!File.Exists(reqPath)) return;

            string id = "?";
            var reply = new Dictionary<string, object>();
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(reqPath));
                File.Delete(reqPath);
                var root = doc.RootElement;
                id = root.GetProperty("id").GetString();
                var op = root.GetProperty("op").GetString();
                reply = Dispatch(op, root, id);
                if (reply == null) return; // Reply comes later (test run).
                reply["ok"] = !reply.ContainsKey("error");
            }
            catch (Exception ex)
            {
                reply["ok"] = false;
                reply["error"] = Unwrap(ex).ToString();
            }
            WriteReply(id, reply);
        }

        private static void WriteReply(string id, Dictionary<string, object> reply)
        {
            reply["id"] = id;
            var path = Path.Combine(Plugin.BridgeDir, "response.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(reply));
            File.Move(path + ".tmp", path, true);
        }

        private Dictionary<string, object> Dispatch(string op, JsonElement req, string id)
        {
            switch (op)
            {
                case "ping": return Status();
                case "exec": return Exec(req.GetProperty("dll").GetString());
                case "screenshot":
                    {
                        var path = req.GetProperty("path").GetString();
                        ScreenCapture.CaptureScreenshot(path);
                        return new Dictionary<string, object> { ["path"] = path };
                    }
                case "test":
                    {
                        var filter = req.TryGetProperty("filter", out var f) ? f.GetString() : null;
                        StartTests(req.GetProperty("dll").GetString(), filter, id);
                        return null;
                    }
                default:
                    return new Dictionary<string, object> { ["error"] = "unknown op: " + op };
            }
        }

        private static Dictionary<string, object> Status()
        {
            return new Dictionary<string, object>
            {
                ["scene"] = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                ["frame"] = Time.frameCount,
                ["timeScale"] = Time.timeScale,
                ["focused"] = Application.isFocused,
                ["unity"] = Application.unityVersion,
                ["gameVersion"] = Application.version,
                ["plugins"] = IL2CPPChainloader.Instance.Plugins.Keys.ToList(),
            };
        }

        private static Dictionary<string, object> Exec(string dll)
        {
            Api.Output.Clear();
            var asm = Assembly.Load(File.ReadAllBytes(dll));
            var run = asm.GetType("__Snippet").GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            var reply = new Dictionary<string, object>();
            try
            {
                var value = run.Invoke(null, null);
                reply["value"] = Api.Format(value);
            }
            catch (Exception ex)
            {
                reply["error"] = Unwrap(ex).ToString();
            }
            reply["output"] = Api.Output.ToString();
            return reply;
        }

        private void StartTests(string dll, string filter, string id)
        {
            var asm = Assembly.Load(File.ReadAllBytes(dll));
            var methods = asm.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.Name.StartsWith("Test") && m.GetParameters().Length == 0)
                .Where(m => filter == null || (m.DeclaringType.Name + "." + m.Name).Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name)
                .ToList();
            _test = new TestRun { Id = id, Queue = methods };
            Plugin.L.LogInfo($"[test] running {methods.Count} tests");
        }

        /// <summary>Advance the test run by one frame. A test may be void or return IEnumerator.</summary>
        private void StepTests()
        {
            if (_test == null) return;
            var run = _test;
            var now = Time.realtimeSinceStartup;

            if (run.Current != null)
            {
                string failure = null;
                bool finished = false;
                try
                {
                    if (now - run.CaseStart > run.CaseTimeout) failure = $"test timeout {run.CaseTimeout}s";
                    else finished = !MoveNextDeep(run.Current);
                }
                catch (Exception ex) { failure = Unwrap(ex).ToString(); }

                if (failure != null || finished) { Record(run, failure); run.Current = null; run.Index++; }
                return;
            }

            if (run.Index >= run.Queue.Count)
            {
                var failed = run.Results.Count(r => !(bool)r["pass"]);
                WriteReply(run.Id, new Dictionary<string, object>
                {
                    ["ok"] = failed == 0,
                    ["total"] = run.Results.Count,
                    ["failed"] = failed,
                    ["results"] = run.Results,
                });
                _test = null;
                return;
            }

            var m = run.Queue[run.Index];
            run.CaseStart = now;
            run.CaseTimeout = 120f;
            try
            {
                var value = m.Invoke(null, null);
                if (value is IEnumerator it) { run.Current = it; return; }
                Record(run, null);
            }
            catch (Exception ex) { Record(run, Unwrap(ex).ToString()); }
            run.Index++;
        }

        private readonly Stack<IEnumerator> _nested = new();

        /// <summary>Runs nested IEnumerators (yield return Wait(1)) without Unity coroutines.</summary>
        private bool MoveNextDeep(IEnumerator root)
        {
            var top = _nested.Count > 0 ? _nested.Peek() : root;
            while (true)
            {
                if (top.MoveNext())
                {
                    if (top.Current is IEnumerator child) { _nested.Push(child); top = child; continue; }
                    return true;
                }
                if (_nested.Count == 0) return false;
                _nested.Pop();
                top = _nested.Count > 0 ? _nested.Peek() : root;
            }
        }

        private void Record(TestRun run, string failure)
        {
            _nested.Clear();
            var m = run.Queue[run.Index];
            var name = m.DeclaringType.Name + "." + m.Name;
            run.Results.Add(new Dictionary<string, object>
            {
                ["name"] = name,
                ["pass"] = failure == null,
                ["error"] = failure,
                ["seconds"] = Math.Round(Time.realtimeSinceStartup - run.CaseStart, 2),
            });
            Plugin.L.LogInfo($"[test] {(failure == null ? "PASS" : "FAIL")} {name}");
        }

        private static Exception Unwrap(Exception ex) =>
            ex is TargetInvocationException t && t.InnerException != null ? t.InnerException : ex;
    }
}
