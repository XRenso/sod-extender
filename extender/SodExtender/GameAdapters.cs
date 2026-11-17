using BepInEx.Logging;

namespace SodExtender
{
    /// <summary>Real adapters of the interfaces in Pure/Abstractions.cs. Thin on purpose: no rules here,
    /// only reads of game fields, because this code cannot run in the offline tests.
    /// The extender itself does not use these yet; its event code is unchanged.</summary>
    public static class GameAdapters
    {
        /// <summary>The in-game clock. Reads SessionData.Instance on each call; safe in the menu (Started = false).</summary>
        public static IGameClock Clock { get; } = new SessionClock();

        /// <summary>Wraps a BepInEx log source, e.g. SodMod.Log.</summary>
        public static IModLog Log(ManualLogSource source) => new BepInLog(source);

        private sealed class SessionClock : IGameClock
        {
            public bool Started => SessionData.Instance != null && SessionData.Instance.startedGame;
            public double GameTime => SessionData.Instance != null ? SessionData.Instance.gameTime : 0;
            public double DecimalClock => SessionData.Instance != null ? SessionData.Instance.decimalClock : 0;
            public long DayKey
            {
                get
                {
                    var s = SessionData.Instance;
                    return s == null ? 0 : SodExtender.GameTime.DayKey(s.monthInt, s.dateInt, s.dayInt);
                }
            }
        }

        private sealed class BepInLog : IModLog
        {
            private readonly ManualLogSource _source;
            public BepInLog(ManualLogSource source) => _source = source;
            public void Info(string message) => _source?.LogInfo(message);
            public void Warning(string message) => _source?.LogWarning(message);
            public void Error(string message) => _source?.LogError(message);
        }
    }
}
