using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SodExtender
{
    public enum ModState { Loaded, Failed }

    public sealed class ModRecord
    {
        public string Name { get; internal set; }
        /// <summary>Unique in this session: settings file name, Harmony id suffix, save data section. See ModConfig.UniqueKey.</summary>
        public string Key { get; internal set; }
        public string Directory { get; internal set; }
        public ModState State { get; internal set; }
        public string Error { get; internal set; }
        public SodMod Instance { get; internal set; }
        /// <summary>The mod's settings (same object as SodMod.Config). Null if the mod failed to load.</summary>
        public BepInEx.Configuration.ConfigFile Config { get; internal set; }
    }

    /// <summary>Finds code mods in the game's mod list and loads them, one failure at a time.</summary>
    public static class Extender
    {
        public const string CodeFolder = "Code";
        internal const string OwnerPrefix = "sodmod.";

        private static readonly List<ModRecord> _mods = new();
        private static readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> _codeDirs = new();
        private static readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> _conflicts = new();
        private static ManualLogSource _log;

        /// <summary>All code mods the extender tried to load, in load order.</summary>
        public static IReadOnlyList<ModRecord> Mods => _mods;

        /// <summary>Methods patched by more than one mod: "Type.Method: ModA, ModB".</summary>
        public static IReadOnlyList<string> Conflicts => _conflicts;

        internal static void Init(ManualLogSource log)
        {
            _log = log;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromModFolders;
        }

        /// <summary>The game can list mods several times (Workshop items arrive late),
        /// so a mod folder loads only the first time it appears.</summary>
        internal static void ScanGameMods()
        {
            var list = ModLoader.Instance == null ? null : ModLoader.Instance.sortedModsList;
            if (list == null) return;

            var changed = false;
            for (var i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                if (entry == null || !entry.enabled) continue;
                var directory = entry.GetContentDirectory();
                if (string.IsNullOrEmpty(directory)) continue;
                var code = Path.Combine(directory, CodeFolder);
                if (!System.IO.Directory.Exists(code) || !_seen.Add(Path.GetFullPath(code))) continue;

                var name = string.IsNullOrEmpty(entry.name) ? Path.GetFileName(directory.TrimEnd('/', '\\')) : entry.name;
                _codeDirs.Add(code);
                LoadMod(name, directory, code);
                changed = true;
            }

            if (!changed) return;
            FindConflicts();
            WriteReport();
        }

        private static void LoadMod(string name, string directory, string code)
        {
            var key = ModConfig.UniqueKey(name, directory, _keys);
            if (key != ModConfig.SafeFileName(name))
                _log.LogWarning($"Mod name '{name}' is used by another mod: this one uses the key '{key}' for settings and save data");
            var record = new ModRecord { Name = name, Key = key, Directory = directory };
            _mods.Add(record);
            var harmony = new Harmony(OwnerPrefix + key);
            try
            {
                var assemblies = System.IO.Directory.GetFiles(code, "*.dll").Select(Assembly.LoadFrom).ToList();
                var modTypes = assemblies.SelectMany(a => a.GetTypes())
                    .Where(t => t.IsSubclassOf(typeof(SodMod)) && !t.IsAbstract).ToList();
                if (modTypes.Count != 1)
                    throw new InvalidOperationException($"expected 1 SodMod class, found {modTypes.Count}");

                foreach (var assembly in assemblies)
                {
                    harmony.PatchAll(assembly);
                    PatchGuard.Register(assembly, record, harmony);
                }
                PatchGuard.Protect(harmony);

                var mod = (SodMod)Activator.CreateInstance(modTypes[0]);
                mod.Name = name;
                mod.Key = key;
                mod.Directory = directory;
                mod.Harmony = harmony;
                mod.Log = BepInEx.Logging.Logger.CreateLogSource(name);
                mod.Config = new BepInEx.Configuration.ConfigFile(ModConfig.PathFor(key), true);
                record.Config = mod.Config;
                mod.OnLoad();

                record.Instance = mod;
                record.State = ModState.Loaded;
                _log.LogInfo($"Loaded mod {name}");
            }
            catch (Exception ex)
            {
                // Undo a partial load: a half-patched mod is worse than a missing mod.
                var unpatch = TryUnpatch(harmony);
                var cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                record.Config = null; // the .cfg file stays on disk, the object is dropped
                record.State = ModState.Failed;
                record.Error = cause.GetType().Name + ": " + cause.Message + unpatch;
                _log.LogError($"Mod {name} failed to load and is off: {cause}");
            }
        }

        private static string TryUnpatch(Harmony harmony)
        {
            try
            {
                harmony.UnpatchSelf();
                return "";
            }
            catch (Exception ex)
            {
                _log.LogError($"Could not remove patches of {harmony.Id}: {ex}");
                return " (patches not removed)";
            }
        }

        /// <summary>Turns off a mod that failed at run time. PatchGuard calls this outside the patched methods.</summary>
        internal static void TurnOff(ModRecord record, Harmony harmony, string reason)
        {
            record.State = ModState.Failed;
            record.Error = reason + (harmony == null ? " (patches removed later)" : TryUnpatch(harmony));
            _log.LogError($"Mod {record.Name} is off for this session: {reason}");
            FindConflicts();
            WriteReport();
        }

        /// <summary>Deferred part of TurnOff (flag [Guard] DeferUnpatch): removes the patches when the game is calm.</summary>
        internal static void FinishTurnOff(ModRecord record, Harmony harmony)
        {
            var unpatch = TryUnpatch(harmony);
            record.Error = record.Error.Replace(" (patches removed later)", unpatch);
            _log.LogInfo($"Mod {record.Name}: deferred unpatch done{unpatch}");
            FindConflicts();
            WriteReport();
        }

        internal static void LogError(string message) => _log.LogError(message);

        private static void FindConflicts()
        {
            _conflicts.Clear();
            foreach (var method in Harmony.GetAllPatchedMethods())
            {
                var owners = Harmony.GetPatchInfo(method).Owners
                    .Where(o => o.StartsWith(OwnerPrefix))
                    .Select(o => o.Substring(OwnerPrefix.Length))
                    .Distinct().ToList();
                if (owners.Count < 2) continue;
                var line = $"{method.DeclaringType?.Name}.{method.Name}: {string.Join(", ", owners)}";
                _conflicts.Add(line);
                _log.LogWarning("Patch overlap: " + line);
            }
        }

        private static void WriteReport()
        {
            try
            {
                var text = new StringBuilder();
                text.AppendLine("Extender\t" + Plugin.Version + "\tcore: " + Plugin.Source);
                foreach (var m in _mods)
                    text.AppendLine($"{m.State}\t{m.Name}\t{m.Error}");
                foreach (var c in _conflicts)
                    text.AppendLine("Overlap\t" + c);
                var folder = Path.Combine(Application.persistentDataPath, "SodExtender");
                System.IO.Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "report.txt"), text.ToString());
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not write the report: " + ex.Message);
            }
        }

        /// <summary>Lets a mod ship its own dependency DLLs in Code/.</summary>
        private static Assembly ResolveFromModFolders(object sender, ResolveEventArgs args)
        {
            var file = new AssemblyName(args.Name).Name + ".dll";
            foreach (var dir in _codeDirs)
            {
                var path = Path.Combine(dir, file);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        }
    }
}
