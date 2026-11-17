using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

// PURE FILE: no game, Unity, BepInEx or Harmony types. Compiled into SodExtender.dll AND into the
// offline test project (dev/tests/offline), which has no game references, so a game type here fails that build.
namespace SodExtender
{
    /// <summary>The save data of one mod: a small key/value store saved next to the game's save file.
    /// Values are stored as JSON, so any type System.Text.Json can handle works.
    /// A mod sees only its own section.</summary>
    public sealed class ModSaveData : IKeyValueStore
    {
        private readonly Dictionary<string, JsonElement> _values = new();

        internal IReadOnlyDictionary<string, JsonElement> Values => _values;
        internal void SetRaw(string key, JsonElement value) => _values[key] = value;

        public int Count => _values.Count;

        public bool Has(string key) => key != null && _values.ContainsKey(key);

        public void Set<T>(string key, T value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            _values[key] = JsonSerializer.SerializeToElement(value);
        }

        /// <summary>Returns the stored value, or the fallback if the key is missing or has another type.</summary>
        public T Get<T>(string key, T fallback = default)
        {
            if (key == null || !_values.TryGetValue(key, out var element)) return fallback;
            try
            {
                var value = JsonSerializer.Deserialize<T>(element);
                return value == null ? fallback : value;
            }
            catch (Exception) { return fallback; }
        }

        public bool Remove(string key) => key != null && _values.Remove(key);

        public void Clear() => _values.Clear();
    }

    /// <summary>Plain rules, no game types: where the mod data file of a save lives.</summary>
    public static class SavePaths
    {
        public const string ModsExtension = ".sodmods";

        /// <summary>"X.sodb" or "X.sod" gives "X.sodmods" in the same folder. Other names give null:
        /// the extender does not guess. Only the last extension is replaced, so dots in the name are safe.</summary>
        public static string ToModsPath(string savePath)
        {
            if (string.IsNullOrWhiteSpace(savePath)) return null;
            var ext = Path.GetExtension(savePath);
            if (!string.Equals(ext, ".sodb", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(ext, ".sod", StringComparison.OrdinalIgnoreCase)) return null;
            return savePath.Substring(0, savePath.Length - ext.Length) + ModsExtension;
        }

        /// <summary>The game path may use '/' (SOD.Common documents this); make it '\'.
        /// The "b" suffix for compressed saves does not matter for the mapping.</summary>
        public static string NormalizeGamePath(string path) => path?.Replace('/', '\\');
    }
}
