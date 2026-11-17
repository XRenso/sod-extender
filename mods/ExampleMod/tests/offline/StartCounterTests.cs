using ExampleMod.Rules;
using SodExtender;
using SodOffline.Fakes;
using static SodOffline.Assert;

/// <summary>Offline tests of ExampleMod's rules (src/Rules). Run: py dev/tools/sod.py unit --filter ExampleMod</summary>
public static class ExampleModStartCounterTests
{
    public static void TestFirstStartIsOne()
    {
        var data = new ModSaveData(); // the real store; it is pure
        Equal(1, StartCounter.Record(data));
        Equal(1, data.Get(StartCounter.Key, 0), "stored");
    }

    public static void TestCountsEachStart()
    {
        var data = new ModSaveData();
        for (var i = 0; i < 5; i++) StartCounter.Record(data);
        Equal(5, data.Get(StartCounter.Key, 0));
    }

    public static void TestBadStoredValueStartsAgainFromOne()
    {
        var data = new ModSaveData();
        data.Set(StartCounter.Key, "not a number"); // e.g. an old or edited .sodmods file
        Equal(1, StartCounter.Record(data));
    }

    public static void TestMessage()
    {
        Equal("This save was started 2 time(s)", StartCounter.Message(2));
    }

    public static void TestWithFakeGame()
    {
        // The SodMod subclass needs BepInEx, so a test uses the same rule the mod's OnGameStarted calls.
        var game = new FakeGame();
        var log = new FakeLog();
        for (var load = 0; load < 3; load++)
            log.Info(StartCounter.Message(StartCounter.Record(game.SaveData)));
        Equal("I This save was started 3 time(s)", log.Lines[^1]);
    }
}
