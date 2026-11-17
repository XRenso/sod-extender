using System.IO;

namespace SodExtender
{
    /// <summary>Where mod settings live. The name rules are in Pure/ModConfig.Names.cs.</summary>
    public static partial class ModConfig
    {
        /// <summary>BepInEx/config/SodMods/&lt;key&gt;.cfg. Pass ModRecord.Key (already safe and unique);
        /// SafeFileName here only guards other callers.</summary>
        public static string PathFor(string modKey)
        {
            var folder = Path.Combine(BepInEx.Paths.ConfigPath, FolderName);
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, SafeFileName(modKey) + ".cfg");
        }
    }
}
