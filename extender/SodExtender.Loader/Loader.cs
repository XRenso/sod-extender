using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SodExtender.Loader
{
    /// <summary>The one BepInEx plugin of the extender. It picks a core (bundled, or from the Workshop item) and starts it.
    /// Keep this file small: players get a new loader only with a new zip. See extender-v1.md "Core from the Workshop".</summary>
    [BepInPlugin("sod.extender", "SOD Extender", LoaderVersion)]
    public class Loader : BasePlugin
    {
        public const string LoaderVersion = "1.0.0";
        public const int LoaderApi = 1;
        private const string GameAppId = "986130";
        /// <summary>Workshop item "SOD Extender (core)".</summary>
        private const string CoreItemId = "3841436986";

        private static Assembly _core;

        public override void Load()
        {
            // Verified 2026-11-17 with a real subscription (suite 32/32). false = always the bundled core.
            var useWorkshop = Config.Bind("Core", "UseWorkshop", true,
                "Load the extender core from its Steam Workshop item when it is not older than the bundled one.").Value;
            var itemId = Config.Bind("Core", "WorkshopItemId", CoreItemId,
                "Steam Workshop item id of the extender core.").Value.Trim();
            var dirOverride = Config.Bind("Core", "DirOverride", "",
                "Developers only: a folder with SodExtender.dll + core.json used instead of the Workshop item.").Value.Trim();

            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
                _core != null && new AssemblyName(args.Name).Name == "SodExtender" ? _core : null;

            var bundledDir = Path.Combine(Path.GetDirectoryName(typeof(Loader).Assembly.Location) ?? "", "core");
            var bundled = Read(bundledDir);
            var workshopDir = dirOverride.Length > 0 ? dirOverride : WorkshopDir(itemId);
            var workshop = workshopDir == null ? null : Read(workshopDir);
            var chosen = CoreSelect.Choose(bundled, workshop, useWorkshop, LoaderApi, out var reason);
            Log.LogInfo($"Loader {LoaderVersion}: {reason}");

            if (chosen != null && chosen != bundled)
            {
                var started = TryStart(chosen, $"workshop {chosen.Version} ({chosen.Dir})", copy: true, out var loaded);
                if (started) return;
                // Two assemblies named SodExtender cannot live side by side: fall back only if the Workshop one never loaded.
                if (loaded) { Log.LogError("The Workshop core loaded but failed. Extender is off this session; set [Core] UseWorkshop = false"); return; }
            }
            if (bundled == null) { Log.LogError($"No bundled core in {bundledDir}: reinstall the SOD Extender zip"); return; }
            TryStart(bundled, $"bundled {bundled.Version}", copy: false, out _);
        }

        private bool TryStart(CoreInfo core, string source, bool copy, out bool loaded)
        {
            loaded = false;
            try
            {
                var dll = Path.Combine(core.Dir, "SodExtender.dll");
                if (copy) dll = CopyToCache(dll, core.Version.ToString());
                var asm = Assembly.LoadFrom(dll);
                loaded = true;
                _core = asm;
                var plugin = asm.GetType("SodExtender.Plugin", throwOnError: true);
                plugin.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)
                      .Invoke(null, new object[] { this, source });
                return true;
            }
            catch (Exception ex)
            {
                var cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Log.LogError($"Core {source} failed to start: {cause}");
                return false;
            }
        }

        private static CoreInfo Read(string dir)
        {
            try
            {
                var json = Path.Combine(dir, "core.json");
                if (!File.Exists(json) || !File.Exists(Path.Combine(dir, "SodExtender.dll"))) return null;
                return CoreInfo.Parse(dir, File.ReadAllText(json));
            }
            catch { return null; }
        }

        /// <summary>steamapps/common/&lt;game&gt; → steamapps/workshop/content/986130/&lt;id&gt;/Core.</summary>
        private static string WorkshopDir(string itemId)
        {
            if (itemId.Length == 0) return null;
            var steamapps = Directory.GetParent(Paths.GameRootPath)?.Parent?.FullName;
            return steamapps == null ? null : Path.Combine(steamapps, "workshop", "content", GameAppId, itemId, "Core");
        }

        /// <summary>Load a copy, so the game does not lock the Workshop files that Steam updates.</summary>
        private static string CopyToCache(string dll, string version)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "ColePowered Games", "Shadows of Doubt", "SodExtender", "core-cache", version);
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, "SodExtender.dll");
            File.Copy(dll, target, overwrite: true);
            return target;
        }
    }
}
