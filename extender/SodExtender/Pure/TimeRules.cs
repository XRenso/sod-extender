using System;

// PURE FILE: see Pure/SaveRules.cs.
namespace SodExtender
{
    /// <summary>Plain rules, no game types: when do hour and day events fire.</summary>
    public sealed class TimeTracker
    {
        private bool _init;
        private int _hour;
        private long _dayKey;

        /// <summary>Forget the previous values: the next Update only records them.</summary>
        public void Reset() => _init = false;

        /// <summary>Reports what changed since the last call. The first call after Reset reports nothing.</summary>
        public void Update(int hour, long dayKey, out bool hourChanged, out bool dayChanged)
        {
            hourChanged = dayChanged = false;
            if (_init)
            {
                hourChanged = hour != _hour;
                dayChanged = dayKey != _dayKey;
            }
            _init = true;
            _hour = hour;
            _dayKey = dayKey;
        }
    }

    /// <summary>Plain rules: pause events only on a real change.</summary>
    public sealed class PauseTracker
    {
        private bool _paused;

        /// <summary>True when the state really changed.</summary>
        public bool Set(bool paused)
        {
            if (_paused == paused) return false;
            _paused = paused;
            return true;
        }
    }

    /// <summary>Plain conversions of game time values. TimeEventsPatch uses the same formulas.</summary>
    public static class GameTime
    {
        /// <summary>Hour 0-23 from SessionData.decimalClock.</summary>
        public static int Hour(double decimalClock) => (int)Math.Floor(decimalClock);

        /// <summary>A number that changes when the date changes, from SessionData.monthInt, dateInt, dayInt.</summary>
        public static long DayKey(int monthInt, int dateInt, int dayInt) => ((long)monthInt * 100 + dateInt) * 10 + dayInt;

        /// <summary>Feeds the tracker from a clock (real adapter in the game, fake in tests).</summary>
        public static void Update(this TimeTracker tracker, IGameClock clock, out bool hourChanged, out bool dayChanged)
            => tracker.Update(Hour(clock.DecimalClock), clock.DayKey, out hourChanged, out dayChanged);
    }
}
