using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;

namespace SodExtender
{
    /// <summary>Per-mod save data. Hook functions are public so in-game tests can call them with temp paths.
    /// Every function catches everything: the extender must never break the game's own load, save or delete.</summary>
    public static class SaveStore
    {
        public const int FormatVersion = 1;

        // All sections, including mods that are not installed now: they are kept on the next save.
        private static readonly Dictionary<string, ModSaveData> _sections = new();

        /// <summary>The section of a mod. The same object stays valid across loads.</summary>
        public static ModSaveData For(string modName)
        {
            if (!_sections.TryGetValue(modName, out var s)) _sections[modName] = s = new ModSaveData();
            return s;
        }

        /// <summary>Names of all sections, installed or not.</summary>
        public static IReadOnlyList<string> SectionNames => _sections.Keys.ToList();

        /// <summary>Load hook. Pass null for a new game. Replaces the contents of every section.</summary>
        public static void OnLoad(string savePath)
        {
            try
            {
                foreach (var s in _sections.Values) s.Clear();
                var file = savePath == null ? null : SavePaths.ToModsPath(savePath);
                if (file == null || !File.Exists(file)) return;
                Read(file);
            }
            catch (Exception ex)
            {
                Extender.LogError($"Mod save data: could not read for '{savePath}': {ex.Message}");
            }
        }

        /// <summary>Save hook. Asks each Loaded mod to fill its section, then writes the file.
        /// A mod that throws is charged in the PatchGuard budget; the others and the file are not affected.</summary>
        public static void OnSave(string savePath)
        {
            try
            {
                var file = SavePaths.ToModsPath(savePath);
                if (file == null)
                {
                    Extender.LogError($"Mod save data: unknown save path '{savePath}', nothing written");
                    return;
                }
                foreach (var record in Extender.Mods.ToList())
                {
                    if (record.State != ModState.Loaded || record.Instance == null) continue;
                    try { record.Instance.OnBeforeSave(); }
                    catch (Exception ex) { PatchGuard.ReportError(record, ex); }
                }
                Write(file);
            }
            catch (Exception ex)
            {
                Extender.LogError($"Mod save data: could not write for '{savePath}': {ex}");
            }
        }

        /// <summary>Delete hook. Call after the game's delete. Removes the mod file only if the save is really gone.</summary>
        public static void OnDeleted(string savePath)
        {
            try
            {
                var file = SavePaths.ToModsPath(savePath);
                if (file == null || File.Exists(savePath)) return;
                if (File.Exists(file)) File.Delete(file);
            }
            catch (Exception ex)
            {
                Extender.LogError($"Mod save data: could not delete data of '{savePath}': {ex.Message}");
            }
        }

        private static void Read(string file)
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("format", out var f) || f.ValueKind != JsonValueKind.Number || f.GetInt32() != FormatVersion ||
                !root.TryGetProperty("mods", out var mods) || mods.ValueKind != JsonValueKind.Object)
            {
                Extender.LogError($"Mod save data: '{file}' has an unknown format, ignored");
                return;
            }
            foreach (var mod in mods.EnumerateObject())
            {
                if (mod.Value.ValueKind != JsonValueKind.Object) continue;
                var section = For(mod.Name);
                foreach (var kv in mod.Value.EnumerateObject())
                    section.SetRaw(kv.Name, kv.Value.Clone());
            }
        }

        private static void Write(string file)
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(file));
            Directory.CreateDirectory(folder);
            var nonEmpty = _sections.Where(p => p.Value.Count > 0).OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
            if (nonEmpty.Count == 0)
            {
                // Nothing to keep: do not leave stale data of an overwritten save.
                if (File.Exists(file)) File.Delete(file);
                return;
            }

            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteNumber("format", FormatVersion);
                w.WriteStartObject("mods");
                foreach (var mod in nonEmpty)
                {
                    w.WriteStartObject(mod.Key);
                    foreach (var kv in mod.Value.Values)
                    {
                        w.WritePropertyName(kv.Key);
                        kv.Value.WriteTo(w);
                    }
                    w.WriteEndObject();
                }
                w.WriteEndObject();
                w.WriteEndObject();
            }

            // Atomic: write a temp file next to the target, then replace.
            var temp = file + ".tmp";
            File.WriteAllBytes(temp, stream.ToArray());
            if (File.Exists(file)) File.Replace(temp, file, null);
            else File.Move(temp, file);
        }
    }

    /// <summary>Hooks into the game. They only extract the path and call SaveStore.</summary>
    [HarmonyPatch(typeof(CityConstructor), nameof(CityConstructor.StartLoading))]
    internal static class SaveLoadPatch
    {
        private static void Prefix()
        {
            try
            {
                var r = RestartSafeController.Instance;
                string path = null;
                if (r != null && r.loadSaveGame && r.saveStateFileInfo != null) path = r.saveStateFileInfo.FullName;
                SaveStore.OnLoad(path);
            }
            catch (Exception ex) { Extender.LogError("Save data load patch failed: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveStateController), nameof(SaveStateController.CaptureSaveStateAsync))]
    internal static class SaveWritePatch
    {
        private static void Prefix(string path)
        {
            try { SaveStore.OnSave(SavePaths.NormalizeGamePath(path)); }
            catch (Exception ex) { Extender.LogError("Save data save patch failed: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(MainMenuController), nameof(MainMenuController.DeleteSave))]
    internal static class SaveDeletePatch
    {
        private static string _pending;

        private static void Prefix()
        {
            _pending = null;
            try
            {
                var m = MainMenuController.Instance;
                _pending = m?.selectedSave?.info?.FullName;
            }
            catch (Exception ex) { Extender.LogError("Save data delete patch failed: " + ex.Message); }
        }

        private static void Postfix()
        {
            var path = _pending;
            _pending = null;
            if (path != null) SaveStore.OnDeleted(path);
        }
    }
}
