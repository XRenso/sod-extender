using System;
using System.Text.Json;

// PURE FILE: no game, Unity, BepInEx or Harmony types. Also compiled into the offline tests.
namespace SodExtender.Loader
{
    /// <summary>What core.json next to a core DLL says.</summary>
    public sealed class CoreInfo
    {
        public string Dir;
        public Version Version;
        public int Api;

        /// <summary>Parses core.json text: {"version":"0.3.0","api":1}. Null when the text is missing or wrong.</summary>
        public static CoreInfo Parse(string dir, string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;
                if (!root.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.String) return null;
                if (!root.TryGetProperty("api", out var a) || a.ValueKind != JsonValueKind.Number || !a.TryGetInt32(out var api)) return null;
                if (!Version.TryParse(v.GetString(), out var version)) return null;
                return new CoreInfo { Dir = dir, Version = version, Api = api };
            }
            catch (JsonException) { return null; }
        }
    }

    /// <summary>Which core to load: the Workshop one only when it is allowed, compatible and not older.</summary>
    public static class CoreSelect
    {
        public static CoreInfo Choose(CoreInfo bundled, CoreInfo workshop, bool useWorkshop, int loaderApi, out string reason)
        {
            if (!useWorkshop) { reason = "Workshop core is off ([Core] UseWorkshop = false)"; return bundled; }
            if (workshop == null) { reason = "no Workshop core found"; return bundled; }
            if (workshop.Api != loaderApi) { reason = $"Workshop core needs loader api {workshop.Api}, this loader has {loaderApi}: update the zip install"; return bundled; }
            if (bundled != null && bundled.Api == loaderApi && workshop.Version < bundled.Version)
            { reason = $"Workshop core {workshop.Version} is older than the bundled {bundled.Version}"; return bundled; }
            reason = $"Workshop core {workshop.Version}";
            return workshop;
        }
    }
}
