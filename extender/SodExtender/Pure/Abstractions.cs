// PURE FILE: see Pure/SaveRules.cs.
//
// Small game-facing interfaces. Mod rules depend on these, not on game types, so the rules can run
// in the offline tests. In the game: real adapters (GameAdapters.cs) and ModSaveData. In tests: fakes
// from dev/tests/offline/Fakes and the real ModSaveData.
namespace SodExtender
{
    /// <summary>A key/value store. In the game: SodMod.SaveData (ModSaveData).</summary>
    public interface IKeyValueStore
    {
        int Count { get; }
        bool Has(string key);
        void Set<T>(string key, T value);
        /// <summary>The stored value, or the fallback if the key is missing or has another type.</summary>
        T Get<T>(string key, T fallback = default);
        bool Remove(string key);
        void Clear();
    }

    /// <summary>Read-only view of the in-game clock. In the game: GameAdapters.Clock (SessionData).</summary>
    public interface IGameClock
    {
        /// <summary>True in gameplay (SessionData.startedGame).</summary>
        bool Started { get; }
        /// <summary>Hours since the start of the game timeline (SessionData.gameTime).</summary>
        double GameTime { get; }
        /// <summary>Time of day, 0 to 24 (SessionData.decimalClock).</summary>
        double DecimalClock { get; }
        /// <summary>Changes when the date changes; see GameTime.DayKey.</summary>
        long DayKey { get; }
    }

    /// <summary>A log. In the game: GameAdapters.Log(SodMod.Log).</summary>
    public interface IModLog
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }

    /// <summary>The event part of SodMod, for rules that react to events. A mod forwards its
    /// SodMod overrides to an object of this type; offline, FakeGame calls it.</summary>
    public interface IModEvents
    {
        void OnGameStarted(bool isNewGame);
        void OnHourChanged(int hour);
        void OnDayChanged();
        void OnPauseChanged(bool paused);
    }
}
