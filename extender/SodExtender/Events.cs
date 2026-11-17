using System;
using HarmonyLib;

namespace SodExtender
{
    /// <summary>Game events for code that is not a SodMod subclass. SodMod virtual methods are the primary API.
    /// A handler that throws is logged and does not stop the other handlers.</summary>
    public static class SodEvents
    {
        public static event Action<bool> GameStarted;
        public static event Action MainMenu;
        public static event Action<int> HourChanged;
        public static event Action DayChanged;
        public static event Action<bool> PauseChanged;

        private static void Raise<T>(Action<T> handlers, T arg, string name)
        {
            if (handlers == null) return;
            foreach (var d in handlers.GetInvocationList())
            {
                try { ((Action<T>)d)(arg); }
                catch (Exception ex) { Extender.LogError($"SodEvents.{name} handler failed: {ex}"); }
            }
        }

        private static void Raise(Action handlers, string name)
        {
            if (handlers == null) return;
            foreach (var d in handlers.GetInvocationList())
            {
                try { ((Action)d)(); }
                catch (Exception ex) { Extender.LogError($"SodEvents.{name} handler failed: {ex}"); }
            }
        }

        internal static void RaiseGameStarted(bool v) => Raise(GameStarted, v, nameof(GameStarted));
        internal static void RaiseMainMenu() => Raise(MainMenu, nameof(MainMenu));
        internal static void RaiseHour(int v) => Raise(HourChanged, v, nameof(HourChanged));
        internal static void RaiseDay() => Raise(DayChanged, nameof(DayChanged));
        internal static void RaisePause(bool v) => Raise(PauseChanged, v, nameof(PauseChanged));
    }

    /// <summary>Calls each Loaded mod in load order. A mod that throws is charged in the PatchGuard budget.</summary>
    internal static class Events
    {
        private static readonly TimeTracker _time = new();
        private static readonly PauseTracker _pause = new();
        private static bool _wasStarted;

        private static void Dispatch(string name, Action<SodMod> call)
        {
            try
            {
                foreach (var record in Extender.Mods)
                {
                    if (record.State != ModState.Loaded || record.Instance == null) continue;
                    try { call(record.Instance); }
                    catch (Exception ex) { PatchGuard.ReportError(record, ex); }
                }
            }
            catch (Exception ex)
            {
                Extender.LogError($"Event {name} dispatch failed: {ex}");
            }
        }

        internal static void GameStarted(bool isNew)
        {
            _time.Reset();
            _wasStarted = true;
            Dispatch("GameStarted", m => m.OnGameStarted(isNew));
            SodEvents.RaiseGameStarted(isNew);
        }

        internal static void MainMenu()
        {
            _wasStarted = false;
            Dispatch("MainMenu", m => m.OnMainMenu());
            SodEvents.RaiseMainMenu();
        }

        internal static void Pause(bool paused)
        {
            if (!_pause.Set(paused)) return;
            Dispatch("PauseChanged", m => m.OnPauseChanged(paused));
            SodEvents.RaisePause(paused);
        }

        internal static void TimeUpdate(int hour, long dayKey)
        {
            _time.Update(hour, dayKey, out var h, out var d);
            if (h)
            {
                Dispatch("HourChanged", m => m.OnHourChanged(hour));
                SodEvents.RaiseHour(hour);
            }
            if (d)
            {
                Dispatch("DayChanged", m => m.OnDayChanged());
                SodEvents.RaiseDay();
            }
        }

        /// <summary>Called every frame: detects gameplay ending (startedGame true to false).</summary>
        internal static void Tick()
        {
            try
            {
                var s = SessionData.Instance;
                if (s == null) return;
                if (_wasStarted && !s.startedGame) MainMenu();
            }
            catch (Exception ex)
            {
                Extender.LogError("Event Tick failed: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(MurderController), nameof(MurderController.OnStartGame))]
    internal static class GameStartedPatch
    {
        private static void Postfix()
        {
            try
            {
                var r = RestartSafeController.Instance;
                // newGameLoadCity: verified true for a new game in an existing city (2026-11-17).
                // generateNew: assumed for a freshly generated city, not verified; both false after a save load.
                Events.GameStarted(r != null && (r.newGameLoadCity || r.generateNew));
            }
            catch (Exception ex) { Extender.LogError("GameStarted patch failed: " + ex); }
        }
    }

    [HarmonyPatch(typeof(SessionData), nameof(SessionData.Update))]
    internal static class TimeEventsPatch
    {
        private static void Postfix(SessionData __instance)
        {
            try
            {
                if (__instance == null || !__instance.startedGame) return;
                var key = ((long)__instance.monthInt * 100 + __instance.dateInt) * 10 + __instance.dayInt;
                Events.TimeUpdate((int)Math.Floor(__instance.decimalClock), key);
            }
            catch (Exception ex) { Extender.LogError("Time events patch failed: " + ex); }
        }
    }

    [HarmonyPatch(typeof(SessionData), nameof(SessionData.PauseGame))]
    internal static class PausePatch
    {
        private static void Postfix()
        {
            try { Events.Pause(true); }
            catch (Exception ex) { Extender.LogError("Pause patch failed: " + ex); }
        }
    }

    [HarmonyPatch(typeof(SessionData), nameof(SessionData.ResumeGame))]
    internal static class ResumePatch
    {
        private static void Postfix()
        {
            try { Events.Pause(false); }
            catch (Exception ex) { Extender.LogError("Resume patch failed: " + ex); }
        }
    }
}
