using System.Collections.Generic;
using SodExtender;

namespace SodOffline.Fakes
{
    /// <summary>Drives IModEvents the way the extender does: the same TimeTracker and PauseTracker
    /// decide when hour, day and pause events fire. Frame() is one SessionData.Update.
    /// Not modelled: the game's own timing (frames per hour, pause on menus), other mods, PatchGuard.</summary>
    public sealed class FakeGame
    {
        public readonly FakeClock Clock = new();
        public readonly ModSaveData SaveData = new();
        private readonly List<IModEvents> _mods = new();
        private readonly TimeTracker _time = new();
        private readonly PauseTracker _pause = new();

        public FakeGame Add(IModEvents mod) { _mods.Add(mod); return this; }

        /// <summary>Like a save load: OnGameStarted, then the time tracker skips its first frame.</summary>
        public void Start(bool isNewGame = false)
        {
            _time.Reset();
            Clock.Started = true;
            foreach (var m in _mods) m.OnGameStarted(isNewGame);
        }

        public void Frame()
        {
            if (!Clock.Started) return;
            _time.Update(Clock, out var hour, out var day);
            if (hour) foreach (var m in _mods) m.OnHourChanged(GameTime.Hour(Clock.DecimalClock));
            if (day) foreach (var m in _mods) m.OnDayChanged();
        }

        /// <summary>Runs a frame, then advances time in steps with a frame after each step.</summary>
        public void Run(double hours, double step = 0.25)
        {
            Frame();
            for (var done = 0.0; done < hours - 1e-9; done += step) { Clock.Advance(step); Frame(); }
        }

        public void SetPaused(bool paused)
        {
            if (!_pause.Set(paused)) return;
            foreach (var m in _mods) m.OnPauseChanged(paused);
        }
    }

    /// <summary>Records every event as text, for asserts.</summary>
    public sealed class EventRecorder : IModEvents
    {
        public readonly List<string> Seen = new();
        public void OnGameStarted(bool isNewGame) => Seen.Add("GameStarted " + isNewGame);
        public void OnHourChanged(int hour) => Seen.Add("Hour " + hour);
        public void OnDayChanged() => Seen.Add("Day");
        public void OnPauseChanged(bool paused) => Seen.Add("Pause " + paused);
    }
}
