using System.Collections.Generic;
using System.Text;

namespace SodExtender
{
    /// <summary>Plain rules for the status panel text (TextMeshPro rich text). No game types.</summary>
    public static class PanelText
    {
        public const string LoadedColour = "#7CE07C";
        public const string FailedColour = "#FF6B6B";
        public const string DimColour = "#B8B8C8";

        /// <summary>Keeps user text from being read as rich-text tags.</summary>
        public static string Safe(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return "<noparse>" + text.Replace("</noparse>", "") + "</noparse>";
        }

        public static string StateWord(bool loaded) => loaded ? "Loaded" : "Failed";

        /// <summary>One mod: name, coloured state, number of settings, then the error on its own line.</summary>
        public static string ModBlock(string name, bool loaded, string error, int settingsCount)
        {
            var colour = loaded ? LoadedColour : FailedColour;
            var sb = new StringBuilder();
            sb.Append("<b>").Append(Safe(name)).Append("</b>   <color=").Append(colour).Append('>')
              .Append(StateWord(loaded)).Append("</color>   <color=").Append(DimColour).Append(">settings: ")
              .Append(settingsCount).Append("</color>");
            if (!loaded && !string.IsNullOrEmpty(error))
                sb.Append("\n<color=").Append(FailedColour).Append('>').Append(Safe(error)).Append("</color>");
            return sb.ToString();
        }

        public static string Summary(int loaded, int failed, int overlaps) =>
            $"Code mods: {loaded} loaded, {failed} failed. Patch overlaps: {overlaps}.";

        /// <summary>Label of a bool setting button.</summary>
        public static string ToggleLabel(string setting, bool value) => setting + ":  " + (value ? "ON" : "OFF");

        public static string Title(string version) => "SOD Extender " + version;

        /// <summary>Overlap lines, or one line saying there are none.</summary>
        public static IList<string> OverlapLines(IEnumerable<string> conflicts)
        {
            var list = new List<string>();
            foreach (var c in conflicts) list.Add(Safe(c));
            if (list.Count == 0) list.Add("<color=" + DimColour + ">No overlaps.</color>");
            return list;
        }
    }
}
