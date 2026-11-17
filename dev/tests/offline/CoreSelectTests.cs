using SodExtender.Loader;
using static SodOffline.Assert;

/// <summary>Loader rules (extender/SodExtender.Loader/Pure/CoreSelect.cs): which core runs.</summary>
public static class CoreSelectTests
{
    private static CoreInfo Core(string dir, string version, int api)
        => CoreInfo.Parse(dir, "{\"version\":\"" + version + "\",\"api\":" + api + "}");

    public static void TestParse()
    {
        var c = Core("d", "0.3.0", 1);
        Check(c != null && c.Version.ToString() == "0.3.0" && c.Api == 1 && c.Dir == "d", "valid core.json");
        Check(CoreInfo.Parse("d", null) == null, "missing");
        Check(CoreInfo.Parse("d", "not json") == null, "garbage");
        Check(CoreInfo.Parse("d", "{\"version\":\"x\",\"api\":1}") == null, "bad version");
        Check(CoreInfo.Parse("d", "{\"version\":\"1.0\"}") == null, "no api");
        Check(CoreInfo.Parse("d", "[1]") == null, "not an object");
    }

    public static void TestFlagOffKeepsBundled()
    {
        var b = Core("b", "0.3.0", 1); var w = Core("w", "0.9.0", 1);
        Check(CoreSelect.Choose(b, w, false, 1, out var r) == b, r);
    }

    public static void TestNewerWorkshopWins()
    {
        var b = Core("b", "0.3.0", 1); var w = Core("w", "0.4.0", 1);
        Check(CoreSelect.Choose(b, w, true, 1, out var r) == w, r);
    }

    public static void TestSameVersionPrefersWorkshop()
    {
        var b = Core("b", "0.3.0", 1); var w = Core("w", "0.3.0", 1);
        Check(CoreSelect.Choose(b, w, true, 1, out _) == w, "equal version: Workshop (proves the update path works)");
    }

    public static void TestOlderWorkshopLoses()
    {
        var b = Core("b", "0.4.0", 1); var w = Core("w", "0.3.0", 1);
        Check(CoreSelect.Choose(b, w, true, 1, out var r) == b && r.Contains("older"), r);
    }

    public static void TestWrongApiLoses()
    {
        var b = Core("b", "0.3.0", 1); var w = Core("w", "2.0.0", 2);
        Check(CoreSelect.Choose(b, w, true, 1, out var r) == b && r.Contains("api"), r);
    }

    public static void TestNoWorkshopOrNoBundled()
    {
        var b = Core("b", "0.3.0", 1); var w = Core("w", "0.3.0", 1);
        Check(CoreSelect.Choose(b, null, true, 1, out _) == b, "no Workshop item");
        Check(CoreSelect.Choose(null, w, true, 1, out _) == w, "bundled missing, Workshop ok");
        Check(CoreSelect.Choose(null, null, true, 1, out _) == null, "nothing");
    }
}
