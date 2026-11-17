using System.Collections.Generic;
using System.Linq;
using SodExtender;
using static SodOffline.Assert;

/// <summary>The extender's pure rules (extender/SodExtender/Pure). Ports of the pure part of the in-game
/// suite (EventTests, ExtenderTests.TestSafeFileName, SaveDataTests path mapping), plus more cases.</summary>
public static class ExtenderRulesTests
{
    public static void TestTimeTrackerSkipsFirstFrame()
    {
        var t = new TimeTracker();
        t.Update(5, 100, out var h, out var d);
        Check(!h && !d, "first update must be silent");
        t.Update(5, 100, out h, out d);
        Check(!h && !d, "no change, no event");
        t.Update(6, 100, out h, out d);
        Check(h && !d, "hour only");
        t.Update(7, 101, out h, out d);
        Check(h && d, "hour and day");
        t.Reset();
        t.Update(1, 5, out h, out d);
        Check(!h && !d, "silent again after Reset");
    }

    public static void TestPauseTrackerFiresOnRealChange()
    {
        var p = new PauseTracker();
        Check(!p.Set(false), "already running");
        Check(p.Set(true), "pause is a change");
        Check(!p.Set(true), "second pause is not");
        Check(p.Set(false), "resume is a change");
    }

    public static void TestGameTimeConversions()
    {
        Equal(0, GameTime.Hour(0.0));
        Equal(13, GameTime.Hour(13.99));
        Equal(23, GameTime.Hour(23.5));
        Check(GameTime.DayKey(1, 2, 3) != GameTime.DayKey(1, 3, 4), "next date gives another key");
        Check(GameTime.DayKey(1, 31, 0) != GameTime.DayKey(2, 1, 1), "next month gives another key");
        Equal(10203L, GameTime.DayKey(10, 20, 3), "formula of TimeEventsPatch");
    }

    public static void TestSafeFileName()
    {
        Equal("My Mod", ModConfig.SafeFileName("My Mod"), "plain name");
        Equal("a_b_c_d", ModConfig.SafeFileName("a/b:c*d"), "invalid chars");
        Equal("x_evil", ModConfig.SafeFileName("x\\evil"), "backslash");
        Equal("x_evil", ModConfig.SafeFileName("x/evil"), "slash");
        Equal("name", ModConfig.SafeFileName("name. "), "trailing dot/space");
        Equal("_CON", ModConfig.SafeFileName("CON"), "reserved name");
        Equal("_com1.txt", ModConfig.SafeFileName("com1.txt"), "reserved name, any case, with extension");
        Equal("_", ModConfig.SafeFileName("  "), "blank");
        Equal("_", ModConfig.SafeFileName(null), "null");
        Equal("_hidden", ModConfig.SafeFileName(".hidden"), "leading dot");
        Equal("Мой мод", ModConfig.SafeFileName("Мой мод"), "cyrillic stays");
    }

    public static void TestSavePathMapping()
    {
        Equal(@"C:\S\a.sodmods", SavePaths.ToModsPath(@"C:\S\a.sodb"), ".sodb");
        Equal(@"C:\S\a.sodmods", SavePaths.ToModsPath(@"C:\S\a.sod"), ".sod");
        Equal(@"C:\S\A.sodmods", SavePaths.ToModsPath(@"C:\S\A.SODB"), "upper case");
        Equal("Benjamin Delgado Franklyn 2023-30-11-18-17-23.30.sodmods",
            SavePaths.ToModsPath("Benjamin Delgado Franklyn 2023-30-11-18-17-23.30.sodb"), "dots in name");
        Equal(@"C:\S\Сохранение. Иван.sodmods", SavePaths.ToModsPath(@"C:\S\Сохранение. Иван.sodb"), "cyrillic");
        Equal(null, SavePaths.ToModsPath(@"C:\S\a.txt"), "other extension");
        Equal(null, SavePaths.ToModsPath(@"C:\S\a"), "no extension");
        Equal(null, SavePaths.ToModsPath(""), "empty");
        Equal(null, SavePaths.ToModsPath(null), "null");
        Equal(@"C:\S\a.sod", SavePaths.NormalizeGamePath("C:/S/a.sod"), "slashes");
    }

    public static void TestModSaveDataRoundTrip()
    {
        var d = new ModSaveData();
        d.Set("Имя", "Иван");
        d.Set("list", new List<int> { 1, 2, 3 });
        d.Set("n", 7);
        Equal(3, d.Count);
        Equal("Иван", d.Get<string>("Имя"));
        Check(d.Get<List<int>>("list").SequenceEqual(new[] { 1, 2, 3 }), "list");
        Equal(7, d.Get("n", 0));
        Equal(-1, d.Get("missing", -1), "missing key gives fallback");
        Equal(5, d.Get("Имя", 5), "wrong type gives fallback");
        Equal(0, d.Get<int>(null), "null key gives fallback");
        Check(d.Remove("n") && !d.Has("n"), "remove");
        Check(!d.Remove(null), "remove null");
        d.Clear();
        Equal(0, d.Count);
    }

    public static void TestModSaveDataIsAKeyValueStore()
    {
        IKeyValueStore s = new ModSaveData();
        s.Set("k", 1.5);
        Equal(1.5, s.Get("k", 0.0));
        Throws<System.ArgumentNullException>(() => s.Set<int>(null, 1), "null key on Set");
    }

    /// <summary>The guard of the whole design: this assembly must not load game, Unity or BepInEx code.</summary>
    public static void TestNoGameAssemblies()
    {
        var bad = typeof(ExtenderRulesTests).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n.StartsWith("Il2Cpp") || n.StartsWith("UnityEngine") || n.StartsWith("BepInEx") ||
                        n == "Assembly-CSharp" || n == "0Harmony")
            .ToList();
        Check(bad.Count == 0, "game assemblies referenced: " + string.Join(", ", bad));
    }
}
