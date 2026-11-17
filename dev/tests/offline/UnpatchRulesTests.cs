using SodExtender;
using static SodOffline.Assert;

/// <summary>StableGate (Pure/UnpatchRules.cs): when the deferred unpatch of a turned-off mod may run.</summary>
public static class UnpatchRulesTests
{
    private static int FramesUntilOpen(StableGate g, bool started, float dt, int max)
    {
        for (int i = 1; i <= max; i++)
            if (g.Update(started, dt)) return i;
        return -1;
    }

    public static void TestGateClosedInMenu()
    {
        var g = new StableGate();
        Equal(-1, FramesUntilOpen(g, false, 0.016f, 2000));
    }

    public static void TestGateOpensAfterCalmGameplay()
    {
        var g = new StableGate();
        int frames = FramesUntilOpen(g, true, 0.1f, 1000);
        Check(frames >= 49 && frames <= 51, $"about 5 s at 10 fps, got {frames} frames");
        Check(g.Update(true, 0.1f), "stays open while calm");
    }

    public static void TestLongFrameResetsGate()
    {
        var g = new StableGate();
        for (int i = 0; i < 45; i++) g.Update(true, 0.1f);
        Check(!g.Update(true, 2.0f), "a load-like frame closes the gate");
        Equal(0f, g.CalmSeconds);
        Check(FramesUntilOpen(g, true, 0.1f, 1000) >= 49, "the full calm time starts again");
    }

    public static void TestLeavingGameResetsGate()
    {
        var g = new StableGate();
        for (int i = 0; i < 60; i++) g.Update(true, 0.1f);
        Check(!g.Update(false, 0.1f), "menu closes the gate");
        Check(!g.Update(true, 0.1f), "a new game needs calm time again");
    }
}

/// <summary>ModConfig.UniqueKey (Pure/ModConfig.Names.cs): settings file, Harmony id and save section per mod.</summary>
public static class ModKeyTests
{
    private static System.Collections.Generic.HashSet<string> Taken() => new();

    public static void TestPlainNameKeepsName()
    {
        var t = Taken();
        Equal("My Mod", ModConfig.UniqueKey("My Mod", "C:/w/111", t));
        Equal("Other", ModConfig.UniqueKey("Other", "C:/w/222", t));
    }

    public static void TestSanitizeCollisionGetsSuffix()
    {
        var t = Taken();
        Equal("a_b", ModConfig.UniqueKey("a/b", "C:/w/111", t));
        var second = ModConfig.UniqueKey("a:b", "C:/w/222", t);
        Check(second.StartsWith("a_b~") && second.Length == "a_b~".Length + 8, "suffix: " + second);
    }

    public static void TestCaseAndSameNameCollide()
    {
        var t = Taken();
        ModConfig.UniqueKey("Mod", "C:/w/1", t);
        Check(ModConfig.UniqueKey("mod", "C:/w/2", t) != "mod", "case-only twin must differ");
        var a = ModConfig.UniqueKey("Mod", "C:/w/3", t);
        var b = ModConfig.UniqueKey("Mod", "C:/w/4", t);
        Check(a != b && a.StartsWith("Mod~") && b.StartsWith("Mod~"), $"same-name twins: {a} {b}");
    }

    public static void TestKeyIsStableAcrossRuns()
    {
        ModConfig.UniqueKey("X", "C:/w/1", Taken());
        var t1 = Taken(); ModConfig.UniqueKey("X", "C:/w/1", t1);
        var t2 = Taken(); ModConfig.UniqueKey("X", "D:/other/1", t2);
        Equal(ModConfig.UniqueKey("X", "C:/w/9/", t1), ModConfig.UniqueKey("X", "D:/other/9", t2), "hash uses the folder name only");
    }

    public static void TestSameFolderTwiceStillUnique()
    {
        var t = Taken();
        ModConfig.UniqueKey("X", "C:/w/1", t);
        var a = ModConfig.UniqueKey("X", "C:/w/9", t);
        var b = ModConfig.UniqueKey("X", "C:/w/9", t);
        Check(a != b, $"{a} {b}");
    }
}
