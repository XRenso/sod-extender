using System.Collections;
using System.Linq;
using SodExtender;
using UnityEngine;
using static SodDevBridge.Api;

/// <summary>Needs the fixture mods deployed (TestBrokenLoad, TestBadPatch, TestThrowPatch, TestDisabled)
/// and a loaded save, because Player.Update runs only in gameplay.</summary>
public static class ExtenderTests
{
    private static ModRecord Find(string name) => Extender.Mods.FirstOrDefault(m => m.Name == name);

    private static int Counter(string typeName)
    {
        var type = System.AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(typeName))
            .FirstOrDefault(t => t != null);
        Check(type != null, typeName + " not found");
        return (int)type.GetField("Calls").GetValue(null);
    }

    private static string CfgPath(string mod) =>
        System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "SodMods", mod + ".cfg");

    private static BepInEx.Configuration.ConfigEntry<bool> LogLineEntry() =>
        (BepInEx.Configuration.ConfigEntry<bool>)Find("ExampleMod").Config["General", "EnabledLogLine"];

    public static void TestSafeFileName()
    {
        Check(ModConfig.SafeFileName("My Mod") == "My Mod", "plain name");
        Check(ModConfig.SafeFileName("a/b:c*d") == "a_b_c_d", "invalid chars: " + ModConfig.SafeFileName("a/b:c*d"));
        Check(ModConfig.SafeFileName("x\\evil") == "x_evil" && ModConfig.SafeFileName("x/evil") == "x_evil", "path separators");
        Check(ModConfig.SafeFileName("name. ") == "name", "trailing dot/space");
        Check(ModConfig.SafeFileName("CON") == "_CON", "reserved name");
        Check(ModConfig.SafeFileName("  ") == "_" && ModConfig.SafeFileName(null) == "_", "empty name");
    }

    public static void TestSettingFileHasDefault()
    {
        var mod = Find("ExampleMod");
        Check(mod != null && mod.Config != null, "ExampleMod has no Config");
        Check(mod.Instance.Config == mod.Config, "SodMod.Config and ModRecord.Config differ");
        Check(mod.Config.ConfigFilePath == CfgPath("ExampleMod"), "wrong path: " + mod.Config.ConfigFilePath);
        Check(System.IO.File.Exists(CfgPath("ExampleMod")), "no .cfg file");
        var text = System.IO.File.ReadAllText(CfgPath("ExampleMod"));
        Check(text.Contains("[General]") && text.Contains("EnabledLogLine ="), "setting not in file: " + text);
        Check(mod.Config.Keys.Any(k => k.Key == "EnabledLogLine"), "setting not listed");
    }

    public static void TestSettingFileMatchesValue()
    {
        // The loaded value must equal the file. To prove the file is read, edit it by hand before a reload.
        var line = System.IO.File.ReadAllLines(CfgPath("ExampleMod")).First(l => l.StartsWith("EnabledLogLine ="));
        var inFile = line.Split('=')[1].Trim();
        Check(string.Equals(inFile, LogLineEntry().Value.ToString(), System.StringComparison.OrdinalIgnoreCase),
            $"file says {inFile}, loaded value is {LogLineEntry().Value}");
    }

    public static void TestSettingChangeIsSaved()
    {
        var entry = LogLineEntry();
        var old = entry.Value;
        try
        {
            entry.Value = !old;
            var text = System.IO.File.ReadAllText(CfgPath("ExampleMod"));
            Check(text.Contains("EnabledLogLine = " + (!old).ToString().ToLowerInvariant()), "change not on disk: " + text);
        }
        finally { entry.Value = old; }
    }

    public static void TestFailedModHasNoConfig()
    {
        var mod = Find("TestBrokenLoad");
        Check(mod != null && mod.Config == null, "failed mod keeps a Config object");
    }

    public static void TestExampleModLoaded()
    {
        var mod = Find("ExampleMod");
        Check(mod != null && mod.State == ModState.Loaded, "ExampleMod is not loaded: " + mod?.Error);
    }

    public static void TestOnLoadFailureIsIsolated()
    {
        var mod = Find("TestBrokenLoad");
        Check(mod != null && mod.State == ModState.Failed, "TestBrokenLoad must be Failed");
        Check(mod.Error.Contains("boom in OnLoad"), "wrong error: " + mod.Error);
    }

    public static void TestFailedModPatchesAreRemoved()
    {
        Check(Extender.Conflicts.All(c => !c.Contains("TestBrokenLoad")), "a patch of the failed mod is still applied");
    }

    public static void TestMissingMethodIsIsolated()
    {
        var mod = Find("TestBadPatch");
        Check(mod != null && mod.State == ModState.Failed, "TestBadPatch must be Failed");
    }

    public static void TestDisabledModIsNotLoaded()
    {
        Check(Find("TestDisabled") == null, "a disabled mod was loaded");
    }

    public static void TestOverlapIsReported()
    {
        var overlap = Extender.Conflicts.FirstOrDefault(c => c.StartsWith("Player.Update"));
        Check(overlap != null && overlap.Contains("ExampleMod") && overlap.Contains("TestOverlap"),
            "no overlap report: " + string.Join("; ", Extender.Conflicts));
    }

    public static IEnumerator TestPatchRunsInGameplay()
    {
        Check(Player.Instance != null, "load a save first: sod.py load \"name\"");
        var start = Counter("ExampleMod.PlayerUpdatePatch");
        yield return Wait(1f);
        Check(Counter("ExampleMod.PlayerUpdatePatch") > start, "the Player.Update patch does not run");
    }

    public static IEnumerator TestThrowingModIsTurnedOff()
    {
        Check(Player.Instance != null, "load a save first: sod.py load \"name\"");
        yield return WaitUntil(() => Find("TestThrowPatch").State == ModState.Failed, 10f);
        Check(Find("TestThrowPatch").Error.Contains("boom in patch"), "wrong error: " + Find("TestThrowPatch").Error);
        // With [Guard] DeferUnpatch the patches go after ~5 s of calm gameplay.
        yield return WaitUntil(() => !Find("TestThrowPatch").Error.Contains("(patches removed later)"), 30f);
        Check(!Find("TestThrowPatch").Error.Contains("(patches removed later)"), "deferred unpatch did not run in 30 s");

        // The patch is gone: its counter stops, and the other mods on the same method keep running.
        var throws = Counter("TestThrowPatch.ThrowingPatch");
        var example = Counter("ExampleMod.PlayerUpdatePatch");
        yield return Wait(1f);
        Check(Counter("TestThrowPatch.ThrowingPatch") == throws, "the patch of the failed mod still runs");
        Check(Counter("ExampleMod.PlayerUpdatePatch") > example, "ExampleMod stopped too");
    }
}
