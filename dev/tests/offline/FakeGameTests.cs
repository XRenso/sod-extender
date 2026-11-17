using System.Linq;
using SodOffline.Fakes;
using static SodOffline.Assert;

/// <summary>Event order from FakeGame. It uses the extender's own TimeTracker/PauseTracker, so these
/// tests check the event rules, not the game's timing.</summary>
public static class FakeGameTests
{
    public static void TestNoTimeEventOnFirstFrameAfterStart()
    {
        var rec = new EventRecorder();
        var game = new FakeGame().Add(rec);
        game.Clock.Advance(10);
        game.Start();
        game.Frame();
        Check(rec.Seen.SequenceEqual(new[] { "GameStarted False" }), string.Join(" | ", rec.Seen));
    }

    public static void TestHourAndDayEventsOverMidnight()
    {
        var rec = new EventRecorder();
        var game = new FakeGame().Add(rec);
        game.Clock.Advance(22.5);
        game.Start();
        game.Run(2); // 22:30 -> 00:30
        Check(rec.Seen.SequenceEqual(new[] { "GameStarted False", "Hour 23", "Hour 0", "Day" }), string.Join(" | ", rec.Seen));
    }

    public static void TestLongRunGivesOneEventPerHour()
    {
        var rec = new EventRecorder();
        var game = new FakeGame().Add(rec);
        game.Start(isNewGame: true);
        game.Run(72);
        Equal(72, rec.Seen.Count(s => s.StartsWith("Hour ")), "hours in 3 days");
        Equal(3, rec.Seen.Count(s => s == "Day"), "days");
        Equal("GameStarted True", rec.Seen[0]);
    }

    public static void TestPauseOnlyOnRealChange()
    {
        var rec = new EventRecorder();
        var game = new FakeGame().Add(rec);
        game.SetPaused(false);
        game.SetPaused(true);
        game.SetPaused(true);
        game.SetPaused(false);
        Check(rec.Seen.SequenceEqual(new[] { "Pause True", "Pause False" }), string.Join(" | ", rec.Seen));
    }

    public static void TestRestartResetsFirstFrame()
    {
        var rec = new EventRecorder();
        var game = new FakeGame().Add(rec);
        game.Start();
        game.Frame();
        game.Clock.Advance(5); // e.g. another save with another time
        game.Start();
        game.Frame();
        Equal(0, rec.Seen.Count(s => s.StartsWith("Hour ")), "a load is not an hour change");
    }
}
