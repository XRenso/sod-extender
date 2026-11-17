using System;
using SodExtender;

namespace SodOffline.Fakes
{
    /// <summary>A clock under test control. Starts at day 1, 00:00. Advance moves time; a pass over
    /// midnight gives a new DayKey. It models only the IGameClock contract, not the game calendar.</summary>
    public sealed class FakeClock : IGameClock
    {
        public bool Started { get; set; } = true;
        public double GameTime { get; private set; }
        public double DecimalClock => GameTime % 24;
        public long DayKey => (long)Math.Floor(GameTime / 24) + 1;

        public FakeClock(double startHour = 0) => GameTime = startHour;

        public void Advance(double hours)
        {
            if (hours < 0) throw new ArgumentOutOfRangeException(nameof(hours));
            GameTime += hours;
        }
    }
}
