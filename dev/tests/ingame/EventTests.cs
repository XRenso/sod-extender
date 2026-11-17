using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SodExtender;
using UnityEngine;
using static SodDevBridge.Api;

/// <summary>Needs fixtures TestEvents and TestThrowEvent deployed and a loaded save.
/// Not covered here (need a second load or a new game inside one run); checked by hand 2026-11-17, see
/// dev/design/extender-v1.md: OnMainMenu (another save loaded from play) and isNewGame=true.</summary>
public static class EventTests
{
    private static List<string> Seen()
    {
        var type = System.AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("TestEvents.Mod")).FirstOrDefault(t => t != null);
        Check(type != null, "fixture TestEvents is not deployed");
        return (List<string>)type.GetField("Seen").GetValue(null);
    }

    private static int ThrowCalls()
    {
        var type = System.AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("TestThrowEvent.Mod")).FirstOrDefault(t => t != null);
        Check(type != null, "fixture TestThrowEvent is not deployed");
        return (int)type.GetField("Calls").GetValue(null);
    }

    private static void NeedGame() =>
        Check(SessionData.Instance != null && SessionData.Instance.startedGame, "load a save first: sod.py load \"name\"");

    // Pure rules.
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

    public static void TestGameStartedFiredOnceWithoutNewGame()
    {
        NeedGame();
        var started = Seen().Where(s => s.StartsWith("GameStarted")).ToList();
        Check(started.Count == 1, "expected 1 GameStarted, got: " + string.Join(",", started));
        Check(started[0] == "GameStarted:False", "a save load is not a new game: " + started[0]);
    }

    public static IEnumerator TestPauseChangedFiresOncePerChange()
    {
        NeedGame();
        var seen = Seen();
        var from = seen.Count;
        SessionData.Instance.PauseGame(false);
        SessionData.Instance.PauseGame(false); // no real change
        yield return Wait(0.5f);
        SessionData.Instance.ResumeGame();
        SessionData.Instance.ResumeGame(); // no real change
        yield return Wait(0.5f);
        var got = seen.Skip(from).Where(s => s.StartsWith("Pause")).ToList();
        Check(got.SequenceEqual(new[] { "Pause:True", "Pause:False" }), "got: " + string.Join(",", got));
    }

    public static IEnumerator TestHourAndDayChangedFire()
    {
        NeedGame();
        var s = SessionData.Instance;
        var seen = Seen();
        var start = s.gameTime;
        var leap = s.leapYearCycle;
        yield return Wait(0.5f);

        var from = seen.Count;
        s.SetGameTime(start + 1f, leap);
        yield return Wait(1f);
        var hourEvents = seen.Skip(from).Where(x => x.StartsWith("Hour")).ToList();
        Check(hourEvents.Count >= 1, "no hour event after +1 hour: " + string.Join(",", seen.Skip(from)));

        from = seen.Count;
        s.SetGameTime(start + 25f, leap); // one day later (+24 from the shifted hour)
        yield return Wait(1f);
        var day = seen.Skip(from).ToList();
        Check(day.Contains("Day"), "no day event after +1 day: " + string.Join(",", day));

        s.SetGameTime(start, leap); // undo
        yield return Wait(1f);
    }

    public static IEnumerator TestThrowingEventTurnsModOffOthersKeepGetting()
    {
        NeedGame();
        var rec = Extender.Mods.FirstOrDefault(m => m.Name == "TestThrowEvent");
        Check(rec != null, "fixture TestThrowEvent is not loaded");
        var seen = Seen();
        if (rec.State == ModState.Loaded)
        {
            for (var i = 0; i < 2; i++) // 4 pause events, 3 errors
            {
                SessionData.Instance.PauseGame(false);
                yield return Wait(0.3f);
                SessionData.Instance.ResumeGame();
                yield return Wait(0.3f);
            }
        }
        yield return WaitUntil(() => rec.State == ModState.Failed, 10f);
        Check(rec.Error.Contains("boom in event"), "wrong error: " + rec.Error);
        var calls = ThrowCalls();
        Check(calls == 3, "expected exactly 3 calls, got " + calls);

        var from = seen.Count;
        SessionData.Instance.PauseGame(false);
        yield return Wait(0.3f);
        SessionData.Instance.ResumeGame();
        yield return Wait(0.3f);
        Check(ThrowCalls() == calls, "a failed mod still gets events");
        Check(seen.Skip(from).Count(x => x.StartsWith("Pause")) == 2, "other mod stopped getting events");
    }
}
