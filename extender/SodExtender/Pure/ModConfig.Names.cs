using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// PURE FILE: see Pure/SaveRules.cs.
namespace SodExtender
{
    public static partial class ModConfig
    {
        public const string FolderName = "SodMods";

        /// <summary>Turns a mod name into a safe file name: invalid characters and trailing dots/spaces become '_'.</summary>
        public static string SafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "_";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var c in name.Trim())
                sb.Append(invalid.Contains(c) || c == '.' && sb.Length == 0 ? '_' : c);
            var text = sb.ToString().TrimEnd('.', ' ');
            if (text.Length == 0) return "_";
            // Windows reserved device names.
            var stem = text.Split('.')[0].ToUpperInvariant();
            var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            return reserved.Contains(stem) ? "_" + text : text;
        }

        /// <summary>A key unique in this session for one mod: its settings file name, Harmony id and save
        /// data section. Usually SafeFileName(name). Two mods can give the same key ("a/b" and "a:b",
        /// "Mod" and "mod" on Windows, or two Workshop items with one name); then the later one gets
        /// "~" + a stable hash of its folder name (the Workshop id), so its key does not change between runs.
        /// Adds the result to <paramref name="taken"/> (compared without case).</summary>
        public static string UniqueKey(string name, string directory, ISet<string> taken)
        {
            var key = SafeFileName(name);
            if (Contains(taken, key))
            {
                var folder = Path.GetFileName((directory ?? "").TrimEnd('/', '\\'));
                key = key + "~" + Fnv1a(folder ?? "").ToString("x8");
                for (var n = 2; Contains(taken, key); n++)
                    key = key.Split('~')[0] + "~" + Fnv1a(folder + "#" + n).ToString("x8");
            }
            taken.Add(key);
            return key;
        }

        private static bool Contains(ISet<string> taken, string key)
            => taken.Any(k => string.Equals(k, key, System.StringComparison.OrdinalIgnoreCase));

        /// <summary>FNV-1a 32 bit: stable across runs (string.GetHashCode is not).</summary>
        private static uint Fnv1a(string text)
        {
            uint h = 2166136261;
            foreach (var c in text) { h ^= c; h *= 16777619; }
            return h;
        }
    }
}
